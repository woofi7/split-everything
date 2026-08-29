using SplitEverything.Application.Contracts.Groups;

namespace SplitEverything.Application.Services;

public interface IInviteService
{
    Task<InviteDto> CreateAsync(Guid userId, Guid groupId, CreateInviteRequest request, CancellationToken ct = default);

    /// <summary>Same invite rendered as a QR code PNG. The token is identical to the emailed link.</summary>
    Task<byte[]> RenderQrCodeAsync(Guid userId, Guid inviteId, int pixelsPerModule = 10, CancellationToken ct = default);

    /// <summary>Unauthenticated peek, so the sign-in page can say which group is being joined.</summary>
    Task<InvitePreviewDto> PreviewAsync(string token, CancellationToken ct = default);

    /// <summary>Redeems after Google sign-in. Claims a placeholder member when the invite names one.</summary>
    Task<RedeemInviteResult> RedeemAsync(Guid userId, string token, CancellationToken ct = default);

    Task RevokeAsync(Guid userId, Guid inviteId, CancellationToken ct = default);
    Task<IReadOnlyList<InviteDto>> ListForGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default);
}
