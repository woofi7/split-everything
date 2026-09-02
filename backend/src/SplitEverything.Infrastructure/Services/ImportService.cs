using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Import;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Algorithms;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Import;
using SplitEverything.Infrastructure.Persistence;
using SplitEverything.Infrastructure.Sync;

namespace SplitEverything.Infrastructure.Services;

/// <summary>
/// Two importers with one shape: upload, map, preview, commit.
///
/// The Settle Up CSV is parsed server-side because it is our own structured
/// format. A bank statement is not: it is parsed entirely in the browser, and this
/// service only ever receives the rows the user confirmed. Nothing here accepts a
/// statement file, by design.
/// </summary>
public sealed class ImportService(
    AppDbContext db,
    ISyncWriter writer,
    IActivityService activity,
    ICurrencyConverter currency,
    IClock clock,
    IGroupService groups) : IImportService
{
    private static readonly Dictionary<string, string[]> HeaderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["date"] = ["date", "datum", "data", "fecha", "when", "day"],
        ["description"] = ["purpose", "description", "what", "zweck", "note", "item", "libelle", "libelle"],
        ["amount"] = ["amount", "betrag", "total", "sum", "montant", "importe", "value"],
        ["currency"] = ["currency", "waehrung", "wahrung", "devise", "moneda", "ccy"],
        ["paidBy"] = ["who paid", "paid by", "payer", "bezahlt von", "paye par", "pagado por"],
        ["participants"] = ["for whom", "participants", "split with", "fuer wen", "fur wen", "pour qui", "beneficiaries"],
        ["splitAmounts"] = ["split amounts", "split amount", "shares", "montants"],
        ["type"] = ["type", "kind", "art"]
    };

    // ---- analysis --------------------------------------------------------

    public async Task<CsvAnalysisResult> AnalyzeCsvAsync(
        Guid userId, Stream csv, string? fileName, CancellationToken ct = default)
    {
        var table = SettleUpCsvReader.Read(csv);

        if (table.Headers.Count == 0)
            throw new ValidationException("That file does not look like a CSV export.");
        if (table.Rows.Count == 0)
            throw new ValidationException("That export has a header row but no expenses.");

        var mapping = GuessMapping(table.Headers);

        var payerColumn = mapping.GetValueOrDefault("paidBy", -1);
        var participantColumn = mapping.GetValueOrDefault("participants", -1);
        var currencyColumn = mapping.GetValueOrDefault("currency", -1);

        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.Rows)
        {
            if (payerColumn >= 0) AddNames(names, CsvValueParser.ParseNameList(Cell(row, payerColumn)));
            if (participantColumn >= 0) AddNames(names, CsvValueParser.ParseNameList(Cell(row, participantColumn)));
        }

        var detectedCurrency = currencyColumn < 0
            ? null
            : table.Rows
                .Select(r => Cell(r, currencyColumn)?.Trim().ToUpperInvariant())
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && c.Length == 3);

        return new CsvAnalysisResult(
            Guid.CreateVersion7(),
            table.Headers,
            table.Rows.Take(5).Select(r => (IReadOnlyList<string>)r.ToList()).ToList(),
            mapping,
            [.. names],
            table.Delimiter,
            detectedCurrency,
            table.Rows.Count);
    }

    public async Task<CsvPreviewResult> PreviewCsvAsync(
        Guid userId, Stream csv, CsvPreviewRequest request, CancellationToken ct = default)
    {
        if (request.GroupId is { } previewGroupId)
            await GroupAccess.RequireMemberAsync(db, userId, previewGroupId, ct);

        var table = SettleUpCsvReader.Read(csv);

        // No group means nothing to match names against and no history to compare,
        // so every name comes back unmapped and nothing is a duplicate.
        var members = request.GroupId is { } forMembers
            ? await LoadMembersAsync(forMembers, ct)
            : new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        // A name bound to an account is answered for, whether or not that account is
        // in the group yet: the import makes it a member. Standing in a placeholder
        // id keeps the rest of this method as it is, and none of it leaves here.
        var nameMapping = new Dictionary<string, Guid?>(
            request.MemberNameMapping, StringComparer.OrdinalIgnoreCase);
        foreach (var name in request.MemberUserMapping?.Keys ?? Enumerable.Empty<string>())
        {
            var trimmed = name?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && nameMapping.GetValueOrDefault(trimmed) is null)
                nameMapping[trimmed] = BoundToAnAccount;
        }

        var parsed = ParseRows(table, request.Mapping, nameMapping, members, request.FallbackCurrency);

        var duplicates = request.GroupId is { } forDuplicates
            ? await FindDuplicatesAsync(userId, parsed.Select(r => r.Fingerprint).ToList(), forDuplicates, ct)
            : [];

        var rows = parsed
            .Select(r => duplicates.TryGetValue(r.Fingerprint, out var match)
                ? r with { IsDuplicate = true, DuplicateOfExpenseId = match.ExpenseId }
                : r)
            .ToList();

        var unmapped = rows
            .SelectMany(ImportRowNames.PeopleIn)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => Resolve(n, nameMapping, members) is null)
            .OrderBy(n => n)
            .ToList();

        return new CsvPreviewResult(
            Guid.CreateVersion7(),
            rows,
            rows.Count(r => r.IsCommittable),
            rows.Count(r => r.Problems.Count > 0),
            rows.Count(r => r.IsDuplicate),
            unmapped);
    }

    public async Task<ImportCommitResult> CommitCsvAsync(
        Guid userId, Stream csv, CsvCommitRequest request, CancellationToken ct = default)
    {
        // Read the file before anything is created. A file that cannot be read must
        // not leave an empty group behind, which would be a worse outcome than the
        // failure itself.
        var table = SettleUpCsvReader.Read(csv);
        if (table.Rows.Count == 0)
            throw new ValidationException("That export has a header row but no expenses.");

        var groupId = request.GroupId ?? await CreateGroupForImportAsync(userId, request, ct);

        var actor = await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);
        GroupAccess.RequireWritable(group);

        var members = await LoadMembersAsync(groupId, ct);
        var nameMapping = new Dictionary<string, Guid?>(request.MemberNameMapping, StringComparer.OrdinalIgnoreCase);
        var createdMemberIds = new List<Guid>();
        var warnings = new List<string>();
        var deviceId = GroupService.DeviceFor(userId);

        // Names bound to an account come first: they decide who the rows belong to,
        // and without them the step below would invent a placeholder wearing the
        // same name and the export would land on a stranger.
        await BindAccountsAsync(
            userId, groupId, request.MemberUserMapping, members, nameMapping,
            createdMemberIds, deviceId, ct);

        var parsed = ParseRows(table, request.Mapping, nameMapping, members, request.FallbackCurrency);

        if (request.CreateMissingMembers)
        {
            var missing = parsed
                .SelectMany(ImportRowNames.PeopleIn)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => Resolve(n, nameMapping, members) is null)
                .ToList();

            foreach (var name in missing)
            {
                var member = new GroupMember
                {
                    GroupId = groupId,
                    DisplayName = name,
                    Role = GroupRole.Member,
                    Status = MembershipStatus.Active,
                    JoinedAt = clock.UtcNow,
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                };
                db.GroupMembers.Add(member);
                await db.SaveChangesAsync(ct);

                await writer.RecordAsync(member, SyncEntityType.GroupMember, groupId,
                    SyncOperation.Create, deviceId, userId, GroupService.MemberPayload(member), ct: ct);

                members[name] = member.Id;
                createdMemberIds.Add(member.Id);
            }

            await db.SaveChangesAsync(ct);

            // Re-parse now that the names resolve, so previously unresolvable rows
            // become committable instead of being skipped.
            parsed = ParseRows(table, request.Mapping, nameMapping, members, request.FallbackCurrency);
        }

        var duplicates = request.SkipDuplicates
            ? await FindDuplicatesAsync(userId, parsed.Select(r => r.Fingerprint).ToList(), groupId, ct)
            : [];

        var skipRows = request.SkipRowNumbers.ToHashSet();
        var batch = new ImportBatch
        {
            GroupId = groupId,
            ImportedByUserId = userId,
            Source = "settleup-csv",
            SourceLabel = request.SourceLabel,
            CommittedAt = clock.UtcNow
        };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        var created = 0;
        var createdSettlements = 0;
        var skipped = 0;
        var seenFingerprints = new HashSet<string>();

        foreach (var row in parsed)
        {
            if (skipRows.Contains(row.RowNumber)) { skipped++; continue; }
            if (!row.IsCommittable)
            {
                skipped++;
                warnings.Add($"Row {row.RowNumber}: {string.Join("; ", row.Problems)}");
                continue;
            }
            if (request.SkipDuplicates
                && (duplicates.ContainsKey(row.Fingerprint) || !seenFingerprints.Add(row.Fingerprint)))
            {
                skipped++;
                continue;
            }

            var rowCurrency = row.Currency ?? group.BaseCurrency;
            var conversion = string.Equals(rowCurrency, group.BaseCurrency, StringComparison.OrdinalIgnoreCase)
                ? new ConversionResult(row.Amount!.Value, 1m, clock.UtcNow)
                : await currency.ConvertAsync(row.Amount!.Value, rowCurrency, group.BaseCurrency, row.SpentAt, ct);

            var participants = row.ParticipantMemberIds.Count > 0
                ? row.ParticipantMemberIds
                : [row.PaidByMemberId!.Value];

            // Who paid, as a list. Settle Up writes a payment several people made
            // together as a pair of lists - payers "Emma;Nicolas", amount "40;25" -
            // and an expense here holds exactly that: one row of 65 that two people
            // put money into, split between whoever it was for.
            var payers = row.Payers is { Count: > 1 }
                ? row.Payers
                : [new ImportPayerShare(row.PaidByName ?? string.Empty, row.PaidByMemberId, row.Amount!.Value)];

            // A transfer is one person paying another down, not money spent. Booked
            // as an expense it would count once as spending and again as a share
            // owed, moving both balances the wrong way.
            //
            // One per payer here, unlike an expense: a settlement is a movement
            // between two people, so two people paying somebody down is two of them.
            if (row.IsSettlement)
            {
                foreach (var payer in payers)
                {
                    if (payer.MemberId is null) continue;

                    var payee = participants.FirstOrDefault(id => id != payer.MemberId.Value);
                    if (payee == Guid.Empty)
                    {
                        skipped++;
                        warnings.Add($"Row {row.RowNumber}: a transfer needs someone to pay.");
                        continue;
                    }

                    var settlement = new Settlement
                    {
                        GroupId = groupId,
                        FromMemberId = payer.MemberId.Value,
                        ToMemberId = payee,
                        Amount = payer.Amount,
                        Currency = rowCurrency,
                        AmountInBaseCurrency = payers.Count == 1
                            ? conversion.Amount
                            : CurrencyPrecision.RoundStored(payer.Amount * conversion.Rate, group.BaseCurrency),
                        SettledAt = row.SpentAt!.Value,
                        Note = row.Description,
                        CreatedAt = clock.UtcNow,
                        UpdatedAt = clock.UtcNow
                    };
                    db.Settlements.Add(settlement);

                    await writer.RecordAsync(settlement, SyncEntityType.Settlement, groupId,
                        SyncOperation.Create, deviceId, userId, SettlementService.SettlementPayload(settlement), ct: ct);

                    createdSettlements++;
                }

                continue;
            }

            // The export's own per-person amounts, used as weights rather than as
            // final figures. An export can disagree with itself by a cent, and this
            // one does: 53.99 split "27;27" adds up to 54.00. Weighting keeps the
            // ratio the export intended while the shares still sum to the total,
            // which the rest of the app requires and the sync path enforces.
            var exact = row.SplitAmounts;
            var useExact = exact is { Count: > 0 }
                           && exact.Count == participants.Count
                           && exact.All(a => a >= 0m)
                           && exact.Sum() > 0m;

            var shares = useExact
                ? SplitCalculator.Calculate(row.Amount!.Value, rowCurrency, SplitType.Shares,
                    participants.Select((id, index) => new SplitInput(id, exact![index])).ToList())
                : SplitCalculator.Calculate(row.Amount!.Value, rowCurrency, SplitType.Equal,
                    participants.Select(id => new SplitInput(id, null)).ToList());

            var known = payers.Where(y => y.MemberId is not null).ToList();
            if (known.Count == 0)
            {
                skipped++;
                warnings.Add($"Row {row.RowNumber}: nobody who paid is a member of this group.");
                continue;
            }

            var expense = new Expense
            {
                GroupId = groupId,
                // The largest contribution is the name on the expense.
                PaidByMemberId = known
                    .OrderByDescending(y => y.Amount)
                    .ThenBy(y => y.MemberId!.Value)
                    .First().MemberId!.Value,
                Description = row.Description,
                Amount = row.Amount!.Value,
                Currency = rowCurrency,
                AmountInBaseCurrency = conversion.Amount,
                ExchangeRate = conversion.Rate,
                ExchangeRateAsOf = conversion.RateAsOf,
                SpentAt = row.SpentAt!.Value,
                SplitType = SplitType.Equal,
                OriginLineageId = group.LineageId,
                ImportFingerprint = row.Fingerprint,
                ImportBatchId = batch.Id,
                Revision = 1,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };
            db.Expenses.Add(expense);

            foreach (var payer in known)
            {
                db.ExpensePayers.Add(new ExpensePayer
                {
                    ExpenseId = expense.Id,
                    GroupId = groupId,
                    MemberId = payer.MemberId!.Value,
                    Amount = payer.Amount,
                    AmountInBaseCurrency = known.Count == 1
                        ? conversion.Amount
                        : CurrencyPrecision.RoundStored(payer.Amount * conversion.Rate, group.BaseCurrency),
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                });
            }

            foreach (var share in shares)
            {
                db.ExpenseSplits.Add(new ExpenseSplit
                {
                    ExpenseId = expense.Id,
                    GroupId = groupId,
                    MemberId = share.MemberId,
                    Amount = share.Amount,
                    AmountInBaseCurrency = CurrencyPrecision.RoundStored(
                        share.Amount * conversion.Rate, group.BaseCurrency),
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                });
            }

            await writer.RecordAsync(expense, SyncEntityType.Expense, groupId,
                SyncOperation.Create, deviceId, userId, ExpenseService.ExpensePayload(expense), ct: ct);

            created++;
        }

        batch.ExpenseCount = created;
        batch.SkippedCount = skipped;

        await activity.RecordAsync(groupId, ActivityKind.ImportCommitted, userId, actor.Id,
            SyncEntityType.Group, groupId,
            $"{actor.DisplayName} imported {created} expenses from a Settle Up export",
            new { created, skipped, request.SourceLabel }, ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return new ImportCommitResult(batch.Id, groupId, created, createdSettlements, skipped, createdMemberIds, warnings);
    }

    /// <summary>
    /// Creates the group an import is going into, when the wizard did not name an
    /// existing one. Goes through the group service rather than inserting a row, so
    /// the owner membership, vector clock, sync log entry and activity all happen
    /// the way they do for a group made by hand.
    /// </summary>
    private async Task<Guid> CreateGroupForImportAsync(
        Guid userId, CsvCommitRequest request, CancellationToken ct)
    {
        var name = request.NewGroupName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name the group this import should create.");

        var created = await groups.CreateAsync(userId, new CreateGroupRequest(
            name,
            request.FallbackCurrency?.Trim().ToUpperInvariant() ?? "CAD",
            null, null, null, []), ct);

        return created.Id;
    }

    public async Task<ImportCommitResult> CommitStatementAsync(
        Guid userId, StatementCommitRequest request, CancellationToken ct = default)
    {
        if (request.Rows.Count == 0)
            throw new ValidationException("There is nothing to import.");

        var deviceId = GroupService.DeviceFor(userId);
        var groupIds = request.Rows.Select(r => r.GroupId).Distinct().ToList();

        var groups = new Dictionary<Guid, Group>();
        foreach (var groupId in groupIds)
        {
            await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);
            var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);
            GroupAccess.RequireWritable(group);
            groups[groupId] = group;
        }

        var duplicates = request.SkipDuplicates
            ? await FindDuplicatesAsync(userId, request.Rows.Select(r => r.Fingerprint).ToList(), null, ct)
            : [];

        var batch = new ImportBatch
        {
            // A statement spans groups, so the batch is not tied to one.
            GroupId = groupIds.Count == 1 ? groupIds[0] : null,
            ImportedByUserId = userId,
            Source = "statement",
            SourceLabel = request.SourceLabel,
            CommittedAt = clock.UtcNow
        };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        var created = 0;
        var skipped = 0;
        var warnings = new List<string>();
        var seen = new HashSet<string>();

        foreach (var row in request.Rows)
        {
            if (request.SkipDuplicates
                && (duplicates.ContainsKey(row.Fingerprint) || !seen.Add(row.Fingerprint)))
            {
                skipped++;
                continue;
            }

            var group = groups[row.GroupId];
            var rowCurrency = GroupAccess.NormalizeCurrency(row.Currency);

            var members = (await db.GroupMembers
                .Where(m => m.GroupId == row.GroupId && !m.IsDeleted)
                .Select(m => m.Id).ToListAsync(ct)).ToHashSet();

            if (!members.Contains(row.PaidByMemberId)
                || row.Splits.Any(s => !members.Contains(s.MemberId)))
            {
                skipped++;
                warnings.Add($"{row.Description}: a participant is not a member of that group.");
                continue;
            }

            var conversion = string.Equals(rowCurrency, group.BaseCurrency, StringComparison.OrdinalIgnoreCase)
                ? new ConversionResult(row.Amount, 1m, clock.UtcNow)
                : await currency.ConvertAsync(row.Amount, rowCurrency, group.BaseCurrency, row.SpentAt, ct);

            IReadOnlyList<SplitShare> shares;
            try
            {
                shares = SplitCalculator.Calculate(row.Amount, rowCurrency, row.SplitType,
                    row.Splits.Select(s => new SplitInput(s.MemberId, s.Value)).ToList());
            }
            catch (ArgumentException ex)
            {
                skipped++;
                warnings.Add($"{row.Description}: {ex.Message}");
                continue;
            }

            var expense = new Expense
            {
                GroupId = row.GroupId,
                PaidByMemberId = row.PaidByMemberId,
                Description = GroupAccess.RequireText(row.Description, "Description", 500),
                Amount = row.Amount,
                Currency = rowCurrency,
                AmountInBaseCurrency = conversion.Amount,
                ExchangeRate = conversion.Rate,
                ExchangeRateAsOf = conversion.RateAsOf,
                SpentAt = row.SpentAt,
                SplitType = row.SplitType,
                Notes = row.Notes,
                OriginLineageId = group.LineageId,
                ImportFingerprint = row.Fingerprint,
                ImportBatchId = batch.Id,
                Revision = 1,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };
            db.Expenses.Add(expense);

            foreach (var share in shares)
            {
                db.ExpenseSplits.Add(new ExpenseSplit
                {
                    ExpenseId = expense.Id,
                    GroupId = row.GroupId,
                    MemberId = share.MemberId,
                    Amount = share.Amount,
                    AmountInBaseCurrency = CurrencyPrecision.RoundStored(
                        share.Amount * conversion.Rate, group.BaseCurrency),
                    InputValue = share.InputValue,
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                });
            }

            await writer.RecordAsync(expense, SyncEntityType.Expense, row.GroupId,
                SyncOperation.Create, deviceId, userId, ExpenseService.ExpensePayload(expense), ct: ct);

            created++;
        }

        batch.ExpenseCount = created;
        batch.SkippedCount = skipped;

        foreach (var groupId in groupIds)
        {
            var actor = await db.GroupMembers.FirstAsync(m => m.GroupId == groupId && m.UserId == userId, ct);
            await activity.RecordAsync(groupId, ActivityKind.ImportCommitted, userId, actor.Id,
                SyncEntityType.Group, groupId,
                $"{actor.DisplayName} imported {created} transactions from a statement",
                new { created, skipped }, ct);
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return new ImportCommitResult(batch.Id, groupIds[0], created, 0, skipped, [], warnings);
    }

    public async Task<DuplicateCheckResult> CheckDuplicatesAsync(
        Guid userId, DuplicateCheckRequest request, CancellationToken ct = default)
    {
        var matches = await FindDuplicatesAsync(userId, request.Fingerprints, request.GroupId, ct);
        return new DuplicateCheckResult(matches.Values.ToList());
    }

    // ---- internals -------------------------------------------------------

    private static IReadOnlyDictionary<string, int> GuessMapping(IReadOnlyList<string> headers)
    {
        var mapping = new Dictionary<string, int>();

        foreach (var (field, aliases) in HeaderAliases)
        {
            var index = -1;
            for (var i = 0; i < headers.Count && index < 0; i++)
            {
                var header = headers[i];
                if (aliases.Any(alias => header.Equals(alias, StringComparison.OrdinalIgnoreCase)))
                    index = i;
            }

            // Fall back to a contains match, for headers such as "Amount (CAD)".
            for (var i = 0; i < headers.Count && index < 0; i++)
            {
                if (aliases.Any(alias => headers[i].Contains(alias, StringComparison.OrdinalIgnoreCase)))
                    index = i;
            }

            mapping[field] = index;
        }

        return mapping;
    }

    private List<ParsedExpenseRow> ParseRows(
        CsvTable table,
        CsvColumnMapping mapping,
        IReadOnlyDictionary<string, Guid?> nameMapping,
        Dictionary<string, Guid> members,
        string? fallbackCurrency)
    {
        var rows = new List<ParsedExpenseRow>(table.Rows.Count);

        for (var i = 0; i < table.Rows.Count; i++)
        {
            var raw = table.Rows[i];
            var problems = new List<string>();

            var spentAt = CsvValueParser.ParseDate(Cell(raw, mapping.DateColumn), mapping.DateFormat);
            if (spentAt is null) problems.Add("the date could not be read");

            // Read as a list, because one cell can hold several: Settle Up writes a
            // payment shared between people as "40;25", one figure per payer. A
            // single amount is a list of one, so this is the same path for both.
            var amounts = CsvValueParser.ParseAmountList(
                Cell(raw, mapping.AmountColumn), mapping.DecimalSeparator);

            decimal? amount = amounts.Count switch
            {
                0 => null,
                1 => amounts[0],
                _ => amounts.Sum()
            };

            if (amount is null) problems.Add("the amount could not be read");
            else if (amount == 0m) problems.Add("the amount is zero");

            var description = Cell(raw, mapping.DescriptionColumn)?.Trim();
            if (string.IsNullOrWhiteSpace(description)) description = "Imported expense";

            var rowCurrency = mapping.CurrencyColumn is { } currencyColumn
                ? Cell(raw, currencyColumn)?.Trim().ToUpperInvariant()
                : null;
            if (string.IsNullOrWhiteSpace(rowCurrency) || rowCurrency.Length != 3)
                rowCurrency = fallbackCurrency?.ToUpperInvariant();

            var payerNames = mapping.PaidByColumn is { } payerColumn
                ? CsvValueParser.ParseNameList(Cell(raw, payerColumn))
                : [];

            var payerName = payerNames.FirstOrDefault();
            var payerId = payerName is null ? null : Resolve(payerName, nameMapping, members);
            if (payerName is null) problems.Add("no payer was named");
            else if (payerId is null) problems.Add($"the payer {payerName} is not a member yet");

            // Several payers, each with their own figure. Flagged rather than
            // guessed when the two lists disagree: a row that says who paid without
            // saying how much each of them put in cannot be divided, and inventing
            // a division would be wrong in a way nobody would see.
            //
            // Decided by the amount cell alone. A payer cell can hold a comma for
            // reasons that have nothing to do with sharing - a name written surname
            // first - and the list of names is split on commas too, so reading two
            // names as two payers would flag ordinary rows as unsplittable.
            List<ImportPayerShare>? payers = null;
            if (amounts.Count > 1)
            {
                if (amounts.Count != payerNames.Count)
                {
                    problems.Add(
                        $"the row names {payerNames.Count} payers and {amounts.Count} amounts");
                }
                else
                {
                    payers = [];
                    for (var payer = 0; payer < payerNames.Count; payer++)
                    {
                        var name = payerNames[payer];
                        var id = Resolve(name, nameMapping, members);
                        if (id is null) problems.Add($"the payer {name} is not a member yet");
                        payers.Add(new ImportPayerShare(name, id, amounts[payer]));
                    }
                }
            }

            // Two export shapes: a single "for whom" cell, or one column per member.
            var participantNames = new List<string>();
            if (mapping.ParticipantColumns is { Count: > 0 })
            {
                foreach (var (column, name) in mapping.ParticipantColumns)
                {
                    var cell = Cell(raw, column);
                    if (!string.IsNullOrWhiteSpace(cell) && cell.Trim() != "0")
                        participantNames.Add(name);
                }
            }
            else
            {
                // The mapped column when there is one. The old fallback guessed the
                // column beside the payer, which in a real export is the amount.
                var participantColumn = mapping.ParticipantsColumn
                                        ?? (mapping.PaidByColumn is null ? 6 : mapping.PaidByColumn.Value + 1);
                participantNames.AddRange(CsvValueParser.ParseNameList(Cell(raw, participantColumn)));
            }

            if (participantNames.Count == 0 && payerName is not null)
                participantNames.Add(payerName);

            var participantIds = new List<Guid>();
            foreach (var name in participantNames)
            {
                var resolved = Resolve(name, nameMapping, members);
                if (resolved is null) problems.Add($"{name} is not a member yet");
                else participantIds.Add(resolved.Value);
            }

            // Paired positionally with the participants, which is how the export
            // writes them. A count that does not line up is not trustworthy, so the
            // split is computed instead.
            var splitAmounts = mapping.SplitAmountsColumn is { } splitColumn
                ? CsvValueParser.ParseAmountList(Cell(raw, splitColumn), mapping.DecimalSeparator)
                : [];
            if (splitAmounts.Count != participantNames.Count) splitAmounts = [];

            var isSettlement = mapping.TypeColumn is { } typeColumn
                               && IsTransfer(Cell(raw, typeColumn));

            var fingerprint = ExpenseFingerprint.Compute(
                spentAt ?? DateTimeOffset.MinValue,
                amount ?? 0m,
                rowCurrency ?? "XXX",
                description);

            rows.Add(new ParsedExpenseRow(
                i + 1, spentAt, description, amount, rowCurrency,
                // Every payer in the name, so a preview of a shared payment does not
                // read as though one person covered the lot.
                payers is null ? payerName : string.Join(", ", payers.Select(p => p.Name)),
                payerId, participantNames, participantIds,
                fingerprint, false, null, problems, splitAmounts, isSettlement, payers));
        }

        return rows;
    }

    /// <summary>
    /// Whether a Type cell marks a settlement. Settle Up calls it a transfer; other
    /// wordings are accepted because the column is free text in practice.
    /// </summary>
    private static bool IsTransfer(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;

        return trimmed.Equals("transfer", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("settlement", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("payment", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, DuplicateMatchDto>> FindDuplicatesAsync(
        Guid userId, IReadOnlyList<string> fingerprints, Guid? groupId, CancellationToken ct)
    {
        if (fingerprints.Count == 0) return [];

        var myGroupIds = await db.GroupMembers
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active && !m.IsDeleted)
            .Select(m => m.GroupId)
            .ToListAsync(ct);

        if (groupId is not null) myGroupIds = myGroupIds.Where(id => id == groupId.Value).ToList();
        if (myGroupIds.Count == 0) return [];

        var distinct = fingerprints.Distinct().ToList();

        var matches = await db.Expenses
            .Where(e => !e.IsDeleted
                        && myGroupIds.Contains(e.GroupId)
                        && e.ImportFingerprint != null
                        && distinct.Contains(e.ImportFingerprint))
            .Select(e => new DuplicateMatchDto(
                e.ImportFingerprint!, e.Id, e.GroupId, e.Group!.Name,
                e.Description, e.Amount, e.SpentAt))
            .ToListAsync(ct);

        return matches
            .GroupBy(m => m.Fingerprint)
            .ToDictionary(g => g.Key, g => g.First());
    }

    private async Task<Dictionary<string, Guid>> LoadMembersAsync(Guid groupId, CancellationToken ct)
    {
        var members = await db.GroupMembers
            .Where(m => m.GroupId == groupId && !m.IsDeleted)
            .Select(m => new { m.Id, m.DisplayName })
            .ToListAsync(ct);

        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members) map[member.DisplayName] = member.Id;
        return map;
    }

    /// <summary>
    /// Makes each account the user matched a name to into a member of the group.
    ///
    /// An export is somebody's group history, and the people in it usually have
    /// accounts here already: binding a name to one means the import lands on the
    /// real person, with their own colour and their own view of the group, rather
    /// than on a placeholder that happens to share their name.
    ///
    /// A membership that already exists is reused, including one that was removed,
    /// for the same reason redeeming an invite does: a second row for one account
    /// would collide with the one-membership-per-user index and orphan whatever
    /// history points at the first.
    /// </summary>
    private async Task BindAccountsAsync(
        Guid userId,
        Guid groupId,
        IReadOnlyDictionary<string, Guid>? mapping,
        Dictionary<string, Guid> members,
        Dictionary<string, Guid?> nameMapping,
        List<Guid> createdMemberIds,
        string deviceId,
        CancellationToken ct)
    {
        if (mapping is null || mapping.Count == 0) return;

        foreach (var (rawName, accountId) in mapping)
        {
            var name = rawName?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var account = await db.Users.FirstOrDefaultAsync(u => u.Id == accountId, ct)
                          ?? throw new NotFoundException($"User {accountId}");

            var existing = await db.GroupMembers
                .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == accountId, ct);

            if (existing is not null)
            {
                if (existing.Status != MembershipStatus.Active || existing.IsDeleted)
                {
                    existing.Status = MembershipStatus.Active;
                    existing.IsDeleted = false;
                    existing.LeftAt = null;
                    await writer.RecordAsync(existing, SyncEntityType.GroupMember, groupId,
                        SyncOperation.Update, deviceId, userId,
                        GroupService.MemberPayload(existing), ct: ct);
                    await db.SaveChangesAsync(ct);
                }

                members[name] = existing.Id;
                nameMapping[name] = existing.Id;
                continue;
            }

            var taken = await db.GroupMembers
                .Where(m => m.GroupId == groupId && m.ColorHex != null)
                .Select(m => m.ColorHex)
                .ToListAsync(ct);

            var member = new GroupMember
            {
                GroupId = groupId,
                UserId = accountId,
                DisplayName = account.DisplayName,
                Role = GroupRole.Member,
                Status = MembershipStatus.Active,
                ColorHex = MemberPalette.Assign(taken),
                JoinedAt = clock.UtcNow,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };
            db.GroupMembers.Add(member);
            await db.SaveChangesAsync(ct);

            await writer.RecordAsync(member, SyncEntityType.GroupMember, groupId,
                SyncOperation.Create, deviceId, userId, GroupService.MemberPayload(member), ct: ct);
            await db.SaveChangesAsync(ct);

            members[name] = member.Id;
            nameMapping[name] = member.Id;
            createdMemberIds.Add(member.Id);
        }
    }

    /// <summary>
    /// Stands for "a name the user has bound to an account" while a preview is
    /// worked out. It is never written anywhere: a preview creates nothing, and by
    /// the time anything is created the real member id is known.
    /// </summary>
    private static readonly Guid BoundToAnAccount = new("00000000-0000-0000-0000-0000000000ff");

    private static Guid? Resolve(
        string name, IReadOnlyDictionary<string, Guid?> nameMapping, Dictionary<string, Guid> members)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();

        // An explicit mapping wins, so the user can fix a typo in the export.
        if (nameMapping.TryGetValue(trimmed, out var mapped) && mapped is not null) return mapped;
        if (members.TryGetValue(trimmed, out var member)) return member;
        return null;
    }

    public async Task<SplitSuggestionResult> GetSplitSuggestionsAsync(
        Guid userId, SplitSuggestionRequest request, CancellationToken ct = default)
    {
        var myGroupIds = await db.GroupMembers
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active && !m.IsDeleted)
            .Select(m => m.GroupId)
            .ToListAsync(ct);

        if (myGroupIds.Count == 0) return new SplitSuggestionResult([]);

        // Only the user's own history is consulted, and only in aggregate: this
        // returns how they usually split a merchant, never anyone else's data.
        var history = await db.Expenses
            .Where(e => myGroupIds.Contains(e.GroupId) && !e.IsDeleted)
            .Select(e => new
            {
                e.Id, e.GroupId, GroupName = e.Group!.Name, e.Description,
                e.SplitType, e.PaidByMemberId, e.SpentAt,
                Splits = e.Splits.Where(s => !s.IsDeleted)
                    .Select(s => new { s.MemberId, s.InputValue }).ToList()
            })
            .ToListAsync(ct);

        var byMerchant = history
            .GroupBy(e => ExpenseFingerprint.NormalizeDescription(e.Description), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var suggestions = new List<SplitSuggestionDto>();

        foreach (var merchant in request.Merchants.Distinct())
        {
            var key = ExpenseFingerprint.NormalizeDescription(merchant);
            if (key.Length == 0 || !byMerchant.TryGetValue(key, out var matches)) continue;

            // The split shape used most often wins; ties go to the most recent.
            var best = matches
                .GroupBy(e => new
                {
                    e.GroupId,
                    e.SplitType,
                    Members = string.Join(",", e.Splits.Select(s => s.MemberId).OrderBy(id => id))
                })
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Max(e => e.SpentAt))
                .First();

            var sample = best.OrderByDescending(e => e.SpentAt).First();

            suggestions.Add(new SplitSuggestionDto(
                key, sample.GroupId, sample.GroupName, sample.SplitType,
                sample.Splits.Select(s => new SplitInputDto(s.MemberId, s.InputValue)).ToList(),
                sample.PaidByMemberId,
                best.Count(), best.Max(e => e.SpentAt)));
        }

        return new SplitSuggestionResult(suggestions);
    }

    public async Task RollbackBatchAsync(Guid userId, Guid batchId, CancellationToken ct = default)
    {
        var batch = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct)
                    ?? throw new NotFoundException($"Import batch {batchId}");

        if (batch.ImportedByUserId != userId)
            throw new ForbiddenException("That import was made by someone else.");
        if (batch.RolledBackAt is not null)
            throw new ValidationException("That import has already been undone.");

        var expenses = await db.Expenses
            .Include(e => e.Payers)
            .Include(e => e.Splits)
            .Where(e => e.ImportBatchId == batchId && !e.IsDeleted)
            .ToListAsync(ct);

        var deviceId = GroupService.DeviceFor(userId);

        foreach (var expense in expenses)
        {
            await writer.RecordAsync(expense, SyncEntityType.Expense, expense.GroupId,
                SyncOperation.Delete, deviceId, userId, ExpenseService.ExpensePayload(expense), ct: ct);

            foreach (var split in expense.Splits.Where(s => !s.IsDeleted))
            {
                split.IsDeleted = true;
                split.DeletedAt = clock.UtcNow;
            }
        }

        batch.RolledBackAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static string? Cell(string[] row, int? index)
        => index is null or < 0 || index >= row.Length ? null : row[index.Value];

    private static void AddNames(SortedSet<string> target, IReadOnlyList<string> names)
    {
        foreach (var name in names) target.Add(name);
    }
}
