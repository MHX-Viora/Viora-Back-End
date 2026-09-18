using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Wallets;

namespace viora_BE.Controllers.Admin;

[ApiController]
[Authorize(Roles = "2")]
[Route("api/admin/finance")]
public sealed class AdminWalletController(IAdminWalletService adminService, IWalletService walletService, IWithdrawalService withdrawalService) : ControllerBase
{
    [HttpGet("wallets")]
    public Task<AdminPage<AdminWalletItem>> Wallets([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null, CancellationToken cancellationToken = default) => adminService.GetWalletsAsync(page, pageSize, keyword, cancellationToken);

    [HttpGet("transactions")]
    public Task<AdminPage<AdminTransactionItem>> Transactions([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null, CancellationToken cancellationToken = default) => adminService.GetTransactionsAsync(page, pageSize, keyword, cancellationToken);

    [HttpGet("payments")]
    public Task<AdminPage<AdminPaymentItem>> Payments([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null, CancellationToken cancellationToken = default) => adminService.GetPaymentsAsync(page, pageSize, keyword, cancellationToken);

    [HttpGet("withdrawals")]
    public Task<WithdrawalPage> Withdrawals([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        withdrawalService.GetAdminPageAsync(page, pageSize, cancellationToken);

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

    [HttpPatch("withdrawals/{id:guid}/status")]
    public async Task<ActionResult<WithdrawalResponse>> ChangeWithdrawalStatus(Guid id, WithdrawalStatusRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await withdrawalService.ChangeStatusAsync(id, request.Status, request.Reason, cancellationToken)); }
        catch (WalletNotFoundException) { return NotFound(new { error = new { code = "WITHDRAWAL_NOT_FOUND", message = "Không tìm thấy yêu cầu rút tiền." } }); }
        catch (WalletValidationException exception) { return UnprocessableEntity(new { error = new { code = exception.Code, message = exception.Message } }); }
        catch (WalletConflictException exception) { return Conflict(new { error = new { code = exception.Code, message = exception.Message } }); }
    }
}

public sealed record AdjustmentRequest(decimal Amount, string Reason, string IdempotencyKey);
public sealed record WithdrawalStatusRequest(Viora.Domain.Entities.WithdrawalStatus Status, string? Reason);
