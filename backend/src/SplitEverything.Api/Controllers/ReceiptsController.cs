using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Services;
using SplitEverything.Infrastructure.Services;

namespace SplitEverything.Api.Controllers;

public sealed class ReceiptsController(
    ICurrentUser currentUser,
    IReceiptService receipts) : ApiControllerBase(currentUser)
{
    [HttpPost]
    [RequestSizeLimit(ReceiptService.MaxBytes)]
    public async Task<ActionResult<ReceiptDto>> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("Attach a file.");

        await using var stream = file.OpenReadStream();
        return Ok(await receipts.UploadAsync(UserId, stream, file.ContentType, file.FileName, ct));
    }

    [HttpGet("{receiptId:guid}")]
    public async Task<IActionResult> Download(Guid receiptId, CancellationToken ct)
    {
        var receipt = await receipts.DownloadAsync(UserId, receiptId, ct);
        return File(receipt.Content, receipt.ContentType, receipt.FileName);
    }
}
