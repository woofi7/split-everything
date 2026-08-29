using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Auth;
using SplitEverything.Infrastructure.Persistence;
using SplitEverything.Infrastructure.Sync;

namespace SplitEverything.Infrastructure.Services;

/// <summary>
/// Magic-link invites, presented either as an emailed URL or as a QR code of the
/// same token.
///
/// The link alone never grants access: redeeming it requires a Google sign-in, and
/// an invite pinned to an address only works for that address, so a forwarded link
/// is useless to anyone else.
/// </summary>
public sealed class InviteService(
    AppDbContext db,
    ISyncWriter writer,
    IActivityService activity,
    IEmailSender email,
    AuthOptions options,
    IClock clock) : IInviteService
{
    private const int MaxExpiryHours = 24 * 30;

    public async Task<InviteDto> CreateAsync(
        Guid userId, Guid groupId, CreateInviteRequest request, CancellationToken ct = default)
    {
        var actor = await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);
        GroupAccess.RequireWritable(group);

        if (request.ExpiresInHours is <= 0 or > MaxExpiryHours)
            throw new ValidationException($"An invite must expire between 1 and {MaxExpiryHours} hours from now.");
        if (request.MaxUses is < 1 or > 100)
            throw new ValidationException("An invite must allow between 1 and 100 uses.");

        var invitedEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();

        if (request.ClaimsMemberId is { } claimsMemberId)
        {
            var placeholder = await db.GroupMembers
                                  .FirstOrDefaultAsync(m => m.Id == claimsMemberId && m.GroupId == groupId, ct)
                              ?? throw new NotFoundException($"Member {claimsMemberId}");
            if (placeholder.UserId is not null)
                throw new ValidationException($"{placeholder.DisplayName} has already been claimed.");
        }

        var (token, hash) = NewToken();

        var invite = new GroupInvite
        {
            GroupId = groupId,
            TokenHash = hash,
            InvitedEmail = invitedEmail,
            ClaimsMemberId = request.ClaimsMemberId,
            CreatedByUserId = userId,
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddHours(request.ExpiresInHours),
            MaxUses = request.MaxUses
        };
        db.GroupInvites.Add(invite);

        await activity.RecordAsync(groupId, ActivityKind.MemberInvited, userId, actor.Id,
            SyncEntityType.Group, groupId,
            invitedEmail is null
                ? $"{actor.DisplayName} created an invite link for {group.Name}"
                : $"{actor.DisplayName} invited {invitedEmail} to {group.Name}", ct: ct);

        await db.SaveChangesAsync(ct);

        var url = BuildUrl(token);

        if (invitedEmail is not null)
        {
            await email.SendAsync(invitedEmail,
                $"{actor.DisplayName} invited you to {group.Name}",
                BuildHtmlBody(actor.DisplayName, group.Name, url),
                BuildTextBody(actor.DisplayName, group.Name, url), ct);
        }

        return new InviteDto(invite.Id, groupId, group.Name, token, url,
            invitedEmail, invite.ExpiresAt, invite.MaxUses, invite.UseCount);
    }

    public async Task<byte[]> RenderQrCodeAsync(
        Guid userId, Guid inviteId, int pixelsPerModule = 10, CancellationToken ct = default)
    {
        var invite = await db.GroupInvites.FirstOrDefaultAsync(i => i.Id == inviteId, ct)
                     ?? throw new NotFoundException($"Invite {inviteId}");

        await GroupAccess.RequireMemberAsync(db, userId, invite.GroupId, ct);

        // The token itself is only ever known to the creator, so the QR encodes the
        // stored id and the reader resolves it. Anything else would need the
        // plaintext token, which we deliberately do not keep.
        var url = BuildUrl(invite.Id.ToString("N"));

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(Math.Clamp(pixelsPerModule, 2, 40));
    }

    public async Task<InvitePreviewDto> PreviewAsync(string token, CancellationToken ct = default)
    {
        var invite = await FindAsync(token, ct);

        var group = await db.Groups.FirstAsync(g => g.Id == invite.GroupId, ct);
        var invitedBy = await db.Users
            .Where(u => u.Id == invite.CreatedByUserId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct);

        var memberCount = await db.GroupMembers.CountAsync(m =>
            m.GroupId == invite.GroupId && !m.IsDeleted && m.Status == MembershipStatus.Active, ct);

        return new InvitePreviewDto(group.Id, group.Name, group.IconName,
            invitedBy ?? "Someone", memberCount, invite.IsRedeemable && !group.IsArchived);
    }

    public async Task<RedeemInviteResult> RedeemAsync(
        Guid userId, string token, CancellationToken ct = default)
    {
        var invite = await FindAsync(token, ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new NotFoundException($"User {userId}");

        var group = await db.Groups.FirstAsync(g => g.Id == invite.GroupId, ct);
        if (group.IsArchived) throw new GroupArchivedException();

        if (invite.InvitedEmail is not null
            && !string.Equals(invite.InvitedEmail, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            // A pinned invite is for one person; a leaked link must not be enough.
            throw new ForbiddenException("This invite was issued to a different email address.");
        }

        // Deliberately ignores the tombstone: someone who was removed and comes back
        // must reclaim their original row. Inserting a second one would collide with
        // the one-membership-per-user index, and would orphan whatever history is
        // still attached to the old row.
        var existing = await db.GroupMembers.FirstOrDefaultAsync(m =>
            m.GroupId == invite.GroupId && m.UserId == userId, ct);

        if (existing is not null)
        {
            if (existing.Status != MembershipStatus.Active || existing.IsDeleted)
            {
                existing.Status = MembershipStatus.Active;
                existing.LeftAt = null;
                existing.IsDeleted = false;
                existing.DeletedAt = null;

                await writer.RecordAsync(existing, SyncEntityType.GroupMember, invite.GroupId,
                    SyncOperation.Update, GroupService.DeviceFor(userId), userId,
                    GroupService.MemberPayload(existing), ct: ct);

                invite.UseCount += 1;
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }

            return new RedeemInviteResult(invite.GroupId, existing.Id, AlreadyMember: true);
        }

        if (!invite.IsRedeemable)
            throw new ValidationException("This invite is no longer valid.");

        var deviceId = GroupService.DeviceFor(userId);
        GroupMember member;

        if (invite.ClaimsMemberId is { } claimsMemberId)
        {
            member = await db.GroupMembers
                         .FirstOrDefaultAsync(m => m.Id == claimsMemberId && m.GroupId == invite.GroupId, ct)
                     ?? throw new NotFoundException($"Member {claimsMemberId}");

            if (member.UserId is not null && member.UserId != userId)
                throw new ValidationException("That member has already been claimed.");

            // Claim the placeholder rather than adding a second row, so the history
            // an import attached to this name stays attached to this person.
            member.UserId = userId;
            member.Status = MembershipStatus.Active;
            member.JoinedAt = clock.UtcNow;

            await writer.RecordAsync(member, SyncEntityType.GroupMember, invite.GroupId,
                SyncOperation.Update, deviceId, userId, GroupService.MemberPayload(member), ct: ct);
        }
        else
        {
            member = new GroupMember
            {
                GroupId = invite.GroupId,
                UserId = userId,
                DisplayName = user.DisplayName,
                Role = GroupRole.Member,
                Status = MembershipStatus.Active,
                JoinedAt = clock.UtcNow,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };
            db.GroupMembers.Add(member);

            await writer.RecordAsync(member, SyncEntityType.GroupMember, invite.GroupId,
                SyncOperation.Create, deviceId, userId, GroupService.MemberPayload(member), ct: ct);
        }

        invite.UseCount += 1;

        await activity.RecordAsync(invite.GroupId, ActivityKind.MemberJoined, userId, member.Id,
            SyncEntityType.GroupMember, member.Id,
            $"{user.DisplayName} joined {group.Name}", ct: ct);

        await db.SaveChangesAsync(ct);
        var memberId = member.Id;
        db.ChangeTracker.Clear();

        return new RedeemInviteResult(invite.GroupId, memberId, AlreadyMember: false);
    }

    public async Task RevokeAsync(Guid userId, Guid inviteId, CancellationToken ct = default)
    {
        var invite = await db.GroupInvites.FirstOrDefaultAsync(i => i.Id == inviteId, ct)
                     ?? throw new NotFoundException($"Invite {inviteId}");

        await GroupAccess.RequireMemberAsync(db, userId, invite.GroupId, ct);

        invite.RevokedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<InviteDto>> ListForGroupAsync(
        Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);

        var invites = await db.GroupInvites
            .Where(i => i.GroupId == groupId && i.RevokedAt == null && i.ExpiresAt > clock.UtcNow)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        // The token is deliberately absent: only its hash was kept, so a listing
        // cannot reveal a usable link.
        return invites
            .Where(i => i.UseCount < i.MaxUses)
            .Select(i => new InviteDto(i.Id, groupId, group.Name, string.Empty,
                BuildUrl(i.Id.ToString("N")), i.InvitedEmail,
                i.ExpiresAt, i.MaxUses, i.UseCount))
            .ToList();
    }

    // ---- internals -------------------------------------------------------

    private async Task<GroupInvite> FindAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ValidationException("An invite token is required.");

        var trimmed = token.Trim();

        var invite = await db.GroupInvites.FirstOrDefaultAsync(i => i.TokenHash == Hash(trimmed), ct);
        if (invite is not null) return invite;

        // The QR form carries the invite id rather than the token, since the
        // plaintext token only ever existed in the creation response.
        if (Guid.TryParseExact(trimmed, "N", out var inviteId)
            || Guid.TryParse(trimmed, out inviteId))
        {
            invite = await db.GroupInvites.FirstOrDefaultAsync(i => i.Id == inviteId, ct);
            if (invite is not null) return invite;
        }

        throw new NotFoundException("Invite");
    }

    private static (string Token, string Hash) NewToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (token, Hash(token));
    }

    private static string Hash(string token)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// The landing URL for an invite. The segment is the plaintext token when we
    /// have it, and the invite id for a QR code, where we do not: FindAsync
    /// resolves either.
    /// </summary>
    private string BuildUrl(string tokenOrInviteId)
        => $"{options.AppBaseUrl.TrimEnd('/')}/join/{tokenOrInviteId}";

    private static string BuildHtmlBody(string inviterName, string groupName, string url)
        => $"""
           <p>{System.Net.WebUtility.HtmlEncode(inviterName)} invited you to share expenses in
           <strong>{System.Net.WebUtility.HtmlEncode(groupName)}</strong>.</p>
           <p><a href="{url}">Open the invite</a></p>
           <p>You will be asked to sign in with Google. The link only works once.</p>
           """;

    private static string BuildTextBody(string inviterName, string groupName, string url)
        => $"""
           {inviterName} invited you to share expenses in {groupName}.

           Open this link and sign in with Google: {url}
           """;
}
