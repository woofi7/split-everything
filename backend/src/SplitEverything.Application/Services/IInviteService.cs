using SplitEverything.Application.Contracts.Groups;

namespace SplitEverything.Application.Services;

public interface IInviteService
{
    Task<InviteDto> CreateAsync(Guid userId, Guid groupId, CreateInviteRequest request, CancellationToken ct = default);

    Task<byte[]> RenderQrCodeAsync(Guid userId, Guid inviteId, int pixelsPerModule = 10, CancellationToken ct = default);

    Task<InvitePreviewDto> PreviewAsync(string token, CancellationToken ct = default);

    Task<RedeemInviteResult> RedeemAsync(Guid userId, string token, CancellationToken ct = default);

    Task RevokeAsync(Guid userId, Guid inviteId, CancellationToken ct = default);
    Task<IReadOnlyList<InviteDto>> ListForGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default);
}
