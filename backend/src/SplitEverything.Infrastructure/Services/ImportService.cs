using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Import;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Algorithms;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Import;
using SplitEverything.Infrastructure.Persistence;
using SplitEverything.Infrastructure.Persistence.Seed;
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
    IClock clock) : IImportService
{
    private static readonly Dictionary<string, string[]> HeaderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["date"] = ["date", "datum", "data", "fecha", "when", "day"],
        ["description"] = ["purpose", "description", "what", "zweck", "note", "item", "libelle", "libelle"],
        ["amount"] = ["amount", "betrag", "total", "sum", "montant", "importe", "value"],
        ["currency"] = ["currency", "waehrung", "wahrung", "devise", "moneda", "ccy"],
        ["category"] = ["category", "kategorie", "categorie", "categoria", "type"],
        ["paidBy"] = ["who paid", "paid by", "payer", "bezahlt von", "paye par", "pagado por"],
        ["participants"] = ["for whom", "participants", "split with", "fuer wen", "fur wen", "pour qui", "beneficiaries"]
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
        await GroupAccess.RequireMemberAsync(db, userId, request.GroupId, ct);

        var table = SettleUpCsvReader.Read(csv);
        var members = await LoadMembersAsync(request.GroupId, ct);
        var parsed = ParseRows(table, request.Mapping, request.MemberNameMapping, members, request.FallbackCurrency);

        var fingerprints = parsed.Select(r => r.Fingerprint).ToList();
        var duplicates = await FindDuplicatesAsync(userId, fingerprints, request.GroupId, ct);

        var rows = parsed
            .Select(r => duplicates.TryGetValue(r.Fingerprint, out var match)
                ? r with { IsDuplicate = true, DuplicateOfExpenseId = match.ExpenseId }
                : r)
            .ToList();

        var unmapped = rows
            .SelectMany(r => r.ParticipantNames.Append(r.PaidByName ?? string.Empty))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => Resolve(n, request.MemberNameMapping, members) is null)
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
        var actor = await GroupAccess.RequireMemberAsync(db, userId, request.GroupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, request.GroupId, ct);
        GroupAccess.RequireWritable(group);

        var table = SettleUpCsvReader.Read(csv);
        var members = await LoadMembersAsync(request.GroupId, ct);
        var nameMapping = new Dictionary<string, Guid?>(request.MemberNameMapping, StringComparer.OrdinalIgnoreCase);
        var createdMemberIds = new List<Guid>();
        var warnings = new List<string>();
        var deviceId = GroupService.DeviceFor(userId);

        var parsed = ParseRows(table, request.Mapping, nameMapping, members, request.FallbackCurrency);

        if (request.CreateMissingMembers)
        {
            var missing = parsed
                .SelectMany(r => r.ParticipantNames.Append(r.PaidByName ?? string.Empty))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => Resolve(n, nameMapping, members) is null)
                .ToList();

            foreach (var name in missing)
            {
                var member = new GroupMember
                {
                    GroupId = request.GroupId,
                    DisplayName = name,
                    Role = GroupRole.Member,
                    Status = MembershipStatus.Active,
                    JoinedAt = clock.UtcNow,
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                };
                db.GroupMembers.Add(member);
                await db.SaveChangesAsync(ct);

                await writer.RecordAsync(member, SyncEntityType.GroupMember, request.GroupId,
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
            ? await FindDuplicatesAsync(userId, parsed.Select(r => r.Fingerprint).ToList(), request.GroupId, ct)
            : [];

        var skipRows = request.SkipRowNumbers.ToHashSet();
        var batch = new ImportBatch
        {
            GroupId = request.GroupId,
            ImportedByUserId = userId,
            Source = "settleup-csv",
            SourceLabel = request.SourceLabel,
            CommittedAt = clock.UtcNow
        };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        var created = 0;
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

            var shares = SplitCalculator.Calculate(row.Amount!.Value, rowCurrency, SplitType.Equal,
                participants.Select(id => new SplitInput(id, null)).ToList());

            var expense = new Expense
            {
                GroupId = request.GroupId,
                PaidByMemberId = row.PaidByMemberId!.Value,
                Description = row.Description,
                Amount = row.Amount!.Value,
                Currency = rowCurrency,
                AmountInBaseCurrency = conversion.Amount,
                ExchangeRate = conversion.Rate,
                ExchangeRateAsOf = conversion.RateAsOf,
                SpentAt = row.SpentAt!.Value,
                CategoryId = await ResolveCategoryAsync(row.CategoryName, ct),
                SplitType = SplitType.Equal,
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
                    GroupId = request.GroupId,
                    MemberId = share.MemberId,
                    Amount = share.Amount,
                    AmountInBaseCurrency = CurrencyPrecision.Round(
                        share.Amount * conversion.Rate, group.BaseCurrency),
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                });
            }

            await writer.RecordAsync(expense, SyncEntityType.Expense, request.GroupId,
                SyncOperation.Create, deviceId, userId, ExpenseService.ExpensePayload(expense), ct: ct);

            created++;
        }

        batch.ExpenseCount = created;
        batch.SkippedCount = skipped;

        await activity.RecordAsync(request.GroupId, ActivityKind.ImportCommitted, userId, actor.Id,
            SyncEntityType.Group, request.GroupId,
            $"{actor.DisplayName} imported {created} expenses from a Settle Up export",
            new { created, skipped, request.SourceLabel }, ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return new ImportCommitResult(batch.Id, request.GroupId, created, skipped, createdMemberIds, warnings);
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
                CategoryId = row.CategoryId,
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
                    AmountInBaseCurrency = CurrencyPrecision.Round(
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

        return new ImportCommitResult(batch.Id, groupIds[0], created, skipped, [], warnings);
    }

    public async Task<DuplicateCheckResult> CheckDuplicatesAsync(
        Guid userId, DuplicateCheckRequest request, CancellationToken ct = default)
    {
        var matches = await FindDuplicatesAsync(userId, request.Fingerprints, request.GroupId, ct);
        return new DuplicateCheckResult(matches.Values.ToList());
    }

    // ---- category rules --------------------------------------------------

    public async Task<IReadOnlyList<CategoryRuleDto>> GetCategoryRulesAsync(
        Guid userId, CancellationToken ct = default)
    {
        await EnsureBuiltInRulesAsync(userId, ct);

        return await db.CategoryRules
            .Where(r => r.UserId == userId && !r.IsDeleted)
            .OrderByDescending(r => r.Weight)
            .ThenBy(r => r.Keyword)
            .Select(r => new CategoryRuleDto(
                r.Id, r.Keyword, r.CategoryId, r.Category!.Key, r.SuggestedGroupId,
                r.Weight, r.HitCount, r.IsEnabled, r.IsBuiltIn))
            .ToListAsync(ct);
    }

    public async Task<CategoryRuleDto> UpsertCategoryRuleAsync(
        Guid userId, UpsertCategoryRuleRequest request, CancellationToken ct = default)
    {
        var keyword = GroupAccess.RequireText(request.Keyword, "Keyword", 120).ToUpperInvariant();

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct)
                       ?? throw new ValidationException("That category does not exist.");

        if (request.SuggestedGroupId is { } groupId)
            await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);

        var rule = request.Id is { } id
            ? await db.CategoryRules.FirstOrDefaultAsync(r => r.Id == id, ct)
            : await db.CategoryRules.FirstOrDefaultAsync(r => r.UserId == userId && r.Keyword == keyword, ct);

        if (rule is not null && rule.UserId != userId)
            throw new ForbiddenException("That rule belongs to another account.");

        if (rule is null)
        {
            rule = new CategoryRule
            {
                UserId = userId,
                Keyword = keyword,
                CategoryId = request.CategoryId,
                IsBuiltIn = false,
                CreatedAt = clock.UtcNow
            };
            db.CategoryRules.Add(rule);
        }
        else
        {
            rule.Keyword = keyword;
            rule.CategoryId = request.CategoryId;
        }

        rule.SuggestedGroupId = request.SuggestedGroupId;
        rule.IsEnabled = request.IsEnabled;
        rule.UpdatedAt = clock.UtcNow;

        // A rule is user preference data and syncs like any other change, so a
        // correction on the phone reaches the laptop.
        rule.Clock = rule.Clock.Tick(GroupService.DeviceFor(userId));
        rule.LastWriterDeviceId = GroupService.DeviceFor(userId);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return new CategoryRuleDto(rule.Id, rule.Keyword, rule.CategoryId, category.Key,
            rule.SuggestedGroupId, rule.Weight, rule.HitCount, rule.IsEnabled, rule.IsBuiltIn);
    }

    public async Task DeleteCategoryRuleAsync(Guid userId, Guid ruleId, CancellationToken ct = default)
    {
        var rule = await db.CategoryRules.FirstOrDefaultAsync(r => r.Id == ruleId, ct)
                   ?? throw new NotFoundException($"Rule {ruleId}");

        if (rule.UserId != userId)
            throw new ForbiddenException("That rule belongs to another account.");

        db.CategoryRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
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
                e.SplitType, e.PaidByMemberId, e.CategoryId, e.SpentAt,
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
                sample.PaidByMemberId, sample.CategoryId,
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

            var amount = CsvValueParser.ParseAmount(Cell(raw, mapping.AmountColumn), mapping.DecimalSeparator);
            if (amount is null) problems.Add("the amount could not be read");
            else if (amount == 0m) problems.Add("the amount is zero");

            var description = Cell(raw, mapping.DescriptionColumn)?.Trim();
            if (string.IsNullOrWhiteSpace(description)) description = "Imported expense";

            var rowCurrency = mapping.CurrencyColumn is { } currencyColumn
                ? Cell(raw, currencyColumn)?.Trim().ToUpperInvariant()
                : null;
            if (string.IsNullOrWhiteSpace(rowCurrency) || rowCurrency.Length != 3)
                rowCurrency = fallbackCurrency?.ToUpperInvariant();

            var payerName = mapping.PaidByColumn is { } payerColumn
                ? CsvValueParser.ParseNameList(Cell(raw, payerColumn)).FirstOrDefault()
                : null;

            var payerId = payerName is null ? null : Resolve(payerName, nameMapping, members);
            if (payerName is null) problems.Add("no payer was named");
            else if (payerId is null) problems.Add($"the payer {payerName} is not a member yet");

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
                var participantColumn = mapping.PaidByColumn is null ? 6 : mapping.PaidByColumn.Value + 1;
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

            var fingerprint = ExpenseFingerprint.Compute(
                spentAt ?? DateTimeOffset.MinValue,
                amount ?? 0m,
                rowCurrency ?? "XXX",
                description);

            rows.Add(new ParsedExpenseRow(
                i + 1, spentAt, description, amount, rowCurrency,
                mapping.CategoryColumn is { } categoryColumn ? Cell(raw, categoryColumn)?.Trim() : null,
                payerName, payerId, participantNames, participantIds,
                fingerprint, false, null, problems));
        }

        return rows;
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

    private async Task<Guid?> ResolveCategoryAsync(string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();

        var byKey = await db.Categories
            .Where(c => c.OwnerUserId == null && c.Key == trimmed.ToLowerInvariant())
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (byKey is not null) return byKey;

        return await db.Categories
            .Where(c => c.OwnerUserId == null && EF.Functions.ILike(c.Name, trimmed))
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Seeds the starter merchant ruleset the first time a user opens the importer.
    /// Done lazily rather than at sign-up so the built-in list can grow without a
    /// migration for existing accounts.
    /// </summary>
    private async Task EnsureBuiltInRulesAsync(Guid userId, CancellationToken ct)
    {
        var existing = await db.CategoryRules
            .Where(r => r.UserId == userId)
            .Select(r => r.Keyword)
            .ToListAsync(ct);
        var have = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = false;
        foreach (var (keyword, categoryKey) in CategorySeed.DefaultMerchantRules)
        {
            if (have.Contains(keyword)) continue;

            db.CategoryRules.Add(new CategoryRule
            {
                UserId = userId,
                Keyword = keyword.ToUpperInvariant(),
                CategoryId = CategorySeed.DeterministicId(categoryKey),
                IsBuiltIn = true,
                IsEnabled = true,
                Weight = 1,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            });
            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    private static string? Cell(string[] row, int? index)
        => index is null or < 0 || index >= row.Length ? null : row[index.Value];

    private static void AddNames(SortedSet<string> target, IReadOnlyList<string> names)
    {
        foreach (var name in names) target.Add(name);
    }
}
