using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Auth;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Auth;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Infrastructure.Services;

/// <summary>
/// Google is the only identity provider; we store no passwords. A verified Google
/// token is exchanged for our own short-lived access token plus a rotating refresh
/// token, so the app can enforce its own session policy without depending on
/// Google's token lifetimes.
/// </summary>
public sealed class AuthService(
    AppDbContext db,
    IJwtTokenService tokens,
    IGoogleTokenVerifier google,
    IInviteService invites,
    AuthOptions options,
    IClock clock) : IAuthService
{
    public async Task<SignInResult> SignInWithGoogleAsync(
        GoogleSignInRequest request, CancellationToken ct = default)
    {
        var identity = await google.VerifyAsync(request.IdToken, ct);

        if (!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email))
        {
            // An unverified address could belong to someone else, and invites are
            // matched on email.
            throw new ForbiddenException("Your Google account email is not verified.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == identity.Subject, ct);
        var isNewUser = user is null;

        if (user is null)
        {
            user = new User
            {
                GoogleSubject = identity.Subject,
                Email = identity.Email.Trim().ToLowerInvariant(),
                DisplayName = FallbackName(identity),
                AvatarUrl = identity.PictureUrl,
                CreatedAt = clock.UtcNow
            };
            db.Users.Add(user);
        }
        else
        {
            // The subject is the identity; email and profile are just current values.
            user.Email = identity.Email.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(identity.Name)) user.DisplayName = identity.Name.Trim();
            if (!string.IsNullOrWhiteSpace(identity.PictureUrl)) user.AvatarUrl = identity.PictureUrl;
        }

        user.LastSeenAt = clock.UtcNow;

        await RegisterDeviceAsync(user, request.DeviceId, ct, request.DeviceLabel, request.Platform);
        await db.SaveChangesAsync(ct);

        var issued = await IssueAsync(user, request.DeviceId, ct);

        // A magic link pinned to this address is the whole point of the invite flow:
        // sign in with Google and land straight in the group.
        var autoJoined = await RedeemPendingInvitesAsync(user, ct);

        db.ChangeTracker.Clear();

        return new SignInResult(Map(user), issued, isNewUser, autoJoined);
    }

    public async Task<SignInResult> SignInAsDeveloperAsync(
        DevelopmentSignInRequest request, CancellationToken ct = default)
    {
        if (!options.AllowDevelopmentSignIn)
        {
            // Checked here rather than only in the controller, so calling the
            // service directly is no way around it.
            throw new ForbiddenException("Development sign-in is not enabled.");
        }

        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (email.Length == 0 || !email.Contains('@') || email.StartsWith('@') || email.EndsWith('@'))
            throw new ValidationException("A valid email address is required.");

        // Namespaced subject: a development account can never collide with a real
        // Google subject, and matching on email would let this take over somebody's
        // actual account.
        var subject = $"dev:{email}";

        var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == subject, ct);
        var isNewUser = user is null;

        if (user is null)
        {
            user = new User
            {
                GoogleSubject = subject,
                Email = email,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                    ? email.Split('@')[0]
                    : request.DisplayName.Trim(),
                CreatedAt = clock.UtcNow
            };
            db.Users.Add(user);
        }
        else if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.DisplayName = request.DisplayName.Trim();
        }

        user.LastSeenAt = clock.UtcNow;

        await RegisterDeviceAsync(user, request.DeviceId, ct);
        await db.SaveChangesAsync(ct);

        var issued = await IssueAsync(user, request.DeviceId, ct);
        var autoJoined = await RedeemPendingInvitesAsync(user, ct);

        db.ChangeTracker.Clear();

        return new SignInResult(Map(user), issued, isNewUser, autoJoined);
    }

    public AuthCapabilities GetCapabilities()
        => new(
            GoogleConfigured: !string.IsNullOrWhiteSpace(options.GoogleClientId),
            DevelopmentSignIn: options.AllowDevelopmentSignIn);

    public async Task<AuthTokens> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new ForbiddenException("A refresh token is required.");

        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null) throw new ForbiddenException("That session is no longer valid.");

        if (stored.RevokedAt is not null)
        {
            if (stored.ReplacedByTokenId is not null)
            {
                // Replay of a token that was rotated: the client already exchanged it,
                // so a second presentation means it leaked. Kill every live token for
                // the account rather than let a thief keep the chain alive.
                await RevokeAllAsync(stored.UserId, ct);
                throw new ForbiddenException("That session was already used. Sign in again.");
            }

            // Revoked without a successor: the user signed this device out
            // deliberately. Refuse it, but leave their other devices signed in.
            throw new ForbiddenException("That session was signed out.");
        }

        if (stored.ExpiresAt <= clock.UtcNow)
            throw new ForbiddenException("That session has expired.");

        var user = stored.User ?? throw new ForbiddenException("That session is no longer valid.");

        var issued = await IssueAsync(user, request.DeviceId ?? stored.DeviceId, ct);

        stored.RevokedAt = clock.UtcNow;
        stored.ReplacedByTokenId = await db.RefreshTokens
            .Where(t => t.TokenHash == tokens.HashRefreshToken(issued.RefreshToken))
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return issued;
    }

    public async Task SignOutAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var hash = tokens.HashRefreshToken(refreshToken);
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.UtcNow), ct);

        db.ChangeTracker.Clear();
    }

    public async Task SignOutAllDevicesAsync(Guid userId, CancellationToken ct = default)
    {
        await RevokeAllAsync(userId, ct);
        db.ChangeTracker.Clear();
    }

    public async Task<AuthenticatedUser> GetMeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new NotFoundException($"User {userId}");
        return Map(user);
    }

    public async Task<AuthenticatedUser> UpdateProfileAsync(
        Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new NotFoundException($"User {userId}");

        if (request.DisplayName is not null)
        {
            var name = GroupAccess.RequireText(request.DisplayName, "Display name", 120);
            if (name != user.DisplayName)
            {
                user.DisplayName = name;
                // Group rows carry their own display name so placeholders can exist;
                // keep the claimed ones in step or a rename would look like it failed.
                await db.GroupMembers
                    .Where(m => m.UserId == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.DisplayName, name), ct);
            }
        }

        if (request.DefaultCurrency is not null)
            user.DefaultCurrency = GroupAccess.NormalizeCurrency(request.DefaultCurrency, "Default currency");
        if (request.PrefersLightTheme is { } light)
            user.PrefersLightTheme = light;
        if (request.PreferredColorHex is not null)
        {
            var wanted = request.PreferredColorHex.Trim();

            // Empty clears it, as with the other clearable fields. Anything else has
            // to be a colour this app hands out, or a group could end up storing a
            // value nothing knows how to render.
            if (wanted.Length == 0) user.PreferredColorHex = null;
            else if (MemberPalette.IsKnown(wanted)) user.PreferredColorHex = wanted;
            else throw new ValidationException("That is not one of the colours to choose from.");
        }

        if (request.Locale is not null)
            user.Locale = request.Locale.Trim();

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return Map(user);
    }

    public async Task<string> ExportMyDataAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new NotFoundException($"User {userId}");

        var memberIds = await db.GroupMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.Id)
            .ToListAsync(ct);

        var groupIds = await db.GroupMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.GroupId)
            .ToListAsync(ct);

        var export = new
        {
            exportedAt = clock.UtcNow,
            user = new { user.Id, user.Email, user.DisplayName, user.DefaultCurrency, user.Locale, user.CreatedAt },
            groups = await db.Groups
                .Where(g => groupIds.Contains(g.Id))
                .Select(g => new
                {
                    g.Id, g.Name, g.BaseCurrency, g.IsArchived, g.CreatedAt,
                    members = g.Members.Select(m => new { m.Id, m.DisplayName, IsMe = m.UserId == userId }).ToList()
                })
                .ToListAsync(ct),
            expenses = await db.Expenses
                .Where(e => groupIds.Contains(e.GroupId)
                            && (e.PaidByMemberId == null || memberIds.Contains(e.PaidByMemberId)
                                || e.Splits.Any(s => memberIds.Contains(s.MemberId))))
                .Select(e => new
                {
                    e.Id, e.GroupId, e.Description, e.Amount, e.Currency,
                    e.AmountInBaseCurrency, e.SpentAt, e.IsDeleted,
                    myShare = e.Splits.Where(s => memberIds.Contains(s.MemberId)).Sum(s => s.Amount),
                    iPaid = memberIds.Contains(e.PaidByMemberId)
                })
                .ToListAsync(ct),
            settlements = await db.Settlements
                .Where(s => memberIds.Contains(s.FromMemberId) || memberIds.Contains(s.ToMemberId))
                .Select(s => new { s.Id, s.GroupId, s.Amount, s.Currency, s.SettledAt, s.Note })
                .ToListAsync(ct),
            comments = await db.ExpenseComments
                .Where(c => memberIds.Contains(c.AuthorMemberId))
                .Select(c => new { c.Id, c.ExpenseId, c.Body, c.CreatedAt })
                .ToListAsync(ct)
        };

        return JsonSerializer.Serialize(export, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    public async Task DeleteMyAccountAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new NotFoundException($"User {userId}");

        // Detach the identity but leave the group rows standing as placeholders:
        // other members' balances are computed from those rows, and erasing them
        // would silently rewrite what everyone else is owed.
        await db.GroupMembers
            .Where(m => m.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.UserId, (Guid?)null)
                .SetProperty(m => m.Status, MembershipStatus.Removed)
                .SetProperty(m => m.LeftAt, clock.UtcNow), ct);

        await db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
        await db.PushSubscriptions.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Devices.Where(d => d.UserId == userId).ExecuteDeleteAsync(ct);

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    // ---- internals -------------------------------------------------------

    /// <summary>
    /// Records the device the sign-in came from. Its id keys every vector clock, so
    /// a device already claimed by another account is refused rather than moved.
    /// </summary>
    private async Task RegisterDeviceAsync(
        User user,
        string? deviceId,
        CancellationToken ct,
        string? label = null,
        string? platform = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;

        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);

        if (device is null)
        {
            db.Devices.Add(new Device
            {
                Id = deviceId,
                UserId = user.Id,
                Label = label,
                Platform = string.IsNullOrWhiteSpace(platform) ? "web" : platform,
                CreatedAt = clock.UtcNow,
                LastSyncedAt = clock.UtcNow
            });
            return;
        }

        if (device.UserId != user.Id)
            throw new ForbiddenException("That device is registered to another account.");

        device.LastSyncedAt = clock.UtcNow;
    }

    private async Task<AuthTokens> IssueAsync(User user, string? deviceId, CancellationToken ct)
    {
        var (accessToken, accessExpiry) = tokens.CreateAccessToken(user, deviceId);
        var (refreshToken, refreshHash, refreshExpiry) = tokens.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = refreshExpiry,
            CreatedAt = clock.UtcNow,
            DeviceId = deviceId
        });

        await db.SaveChangesAsync(ct);

        return new AuthTokens(accessToken, accessExpiry, refreshToken, refreshExpiry);
    }

    private async Task<IReadOnlyList<Guid>> RedeemPendingInvitesAsync(User user, CancellationToken ct)
    {
        var pending = await db.GroupInvites
            .Where(i => i.InvitedEmail == user.Email
                        && i.RevokedAt == null
                        && i.ExpiresAt > clock.UtcNow
                        && i.UseCount < i.MaxUses)
            .Select(i => i.Id)
            .ToListAsync(ct);

        var joined = new List<Guid>();

        foreach (var inviteId in pending)
        {
            try
            {
                var result = await invites.RedeemAsync(user.Id, inviteId.ToString("N"), ct);
                if (!result.AlreadyMember) joined.Add(result.GroupId);
            }
            catch (AppException)
            {
                // A stale or already-claimed invite must not block the sign-in itself.
            }
        }

        return joined;
    }

    private async Task RevokeAllAsync(Guid userId, CancellationToken ct)
        => await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.UtcNow), ct);

    private static string FallbackName(GoogleIdentity identity)
        => string.IsNullOrWhiteSpace(identity.Name)
            ? identity.Email.Split('@')[0]
            : identity.Name.Trim();

    private static AuthenticatedUser Map(User user)
        => new(user.Id, user.Email, user.DisplayName, user.AvatarUrl,
            user.DefaultCurrency, user.PrefersLightTheme, user.PreferredColorHex);
}
