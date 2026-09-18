using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Wallets;

namespace viora_BE.Controllers.Admin;

[ApiController]
[Authorize(Roles = "2")]
[Route("api/admin/finance")]
public sealed class AdminWalletController(IAdminWalletService adminService, IWalletService walletService) : ControllerBase
{
    [HttpGet("wallets")]
    public Task<AdminPage<AdminWalletItem>> Wallets([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null, CancellationToken cancellationToken = default) => adminService.GetWalletsAsync(page, pageSize, keyword, cancellationToken);

    [HttpGet("transactions")]
    public Task<AdminPage<AdminTransactionItem>> Transactions([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null, CancellationToken cancellationToken = default) => adminService.GetTransactionsAsync(page, pageSize, keyword, cancellationToken);

    [HttpGet("payments")]
    public Task<AdminPage<AdminPaymentItem>> Payments([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null, CancellationToken cancellationToken = default) => adminService.GetPaymentsAsync(page, pageSize, keyword, cancellationToken);

    [HttpPost("wallets/{userId:guid}/adjustments")]
    public async Task<ActionResult<WalletTransactionResponse>> Adjust(Guid userId, AdjustmentRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("user_id"), out var adminId)) return Unauthorized();
        try
        {
            return Ok(await walletService.AdjustAsync(userId, adminId, request.Amount, request.Reason, request.IdempotencyKey, cancellationToken));
        }
        catch (WalletValidationException exception)
        {
            return UnprocessableEntity(new { error = new { code = exception.Code, message = exception.Message } });
        }
    }
}

public sealed record AdjustmentRequest(decimal Amount, string Reason, string IdempotencyKey);
