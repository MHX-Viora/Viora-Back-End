using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Wallets;

namespace viora_BE.Controllers.Admin;

[ApiController]
[Authorize(Roles = "2")]
[Route("api/admin/finance")]
public sealed class AdminWalletController(IAdminWalletService adminService, IWalletService walletService, IWithdrawalService withdrawalService) : ControllerBase
{
    [HttpGet("withdrawal-fee-settings")]
    public Task<WithdrawalFeeSettingsResponse> FeeSettings(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return withdrawalService.GetFeeSettingsAsync(cancellationToken);
    }

    [HttpPut("withdrawal-fee-settings")]
    public async Task<ActionResult<WithdrawalFeeSettingsResponse>> UpdateFeeSettings(WithdrawalFeeSettingsRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("user_id"), out var adminId)) return Unauthorized();
        try { return Ok(await withdrawalService.UpdateFeeSettingsAsync(adminId, request.FeePercent, request.ExpectedVersion, cancellationToken)); }
        catch (WalletValidationException exception) { return UnprocessableEntity(new { error = new { code = exception.Code, message = exception.Message } }); }
        catch (WalletConflictException exception) { return Conflict(new { error = new { code = exception.Code, message = exception.Message } }); }
    }
    [HttpGet("wallets")]
    public Task<AdminPage<AdminWalletItem>> Wallets([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null, CancellationToken cancellationToken = default) => adminService.GetWalletsAsync(page, pageSize, keyword, cancellationToken);

    [HttpGet("transactions")]
    public Task<AdminPage<AdminTransactionItem>> Transactions([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null, CancellationToken cancellationToken = default) => adminService.GetTransactionsAsync(page, pageSize, keyword, cancellationToken);

    [HttpGet("payments")]
    public Task<AdminPage<AdminPaymentItem>> Payments([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null, CancellationToken cancellationToken = default) => adminService.GetPaymentsAsync(page, pageSize, keyword, cancellationToken);

    [HttpGet("withdrawals")]
    public Task<WithdrawalPage> Withdrawals([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null, [FromQuery] Viora.Domain.Entities.WithdrawalStatus? status = null, CancellationToken cancellationToken = default) =>
        withdrawalService.GetAdminPageAsync(page, pageSize, cancellationToken, keyword, status);

    [HttpGet("withdrawals/{id:guid}")]
    public async Task<ActionResult<AdminWithdrawalDetail>> WithdrawalDetail(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var detail = await withdrawalService.GetAdminDetailAsync(id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

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
        catch (WalletConflictException exception) { return Conflict(new { error = new { code = exception.Code, message = exception.Message } }); }
    }

    [HttpPatch("withdrawals/{id:guid}/status")]
    public async Task<ActionResult<WithdrawalResponse>> ChangeWithdrawalStatus(Guid id, WithdrawalStatusRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("user_id"), out var adminId)) return Unauthorized();
        if (request.Status is not (Viora.Domain.Entities.WithdrawalStatus.Processing or Viora.Domain.Entities.WithdrawalStatus.Completed or Viora.Domain.Entities.WithdrawalStatus.Rejected or Viora.Domain.Entities.WithdrawalStatus.Failed)) return BadRequest();
        try { return Ok(await withdrawalService.ChangeStatusAsync(id, request.Status, request.Reason, cancellationToken, adminId)); }
        catch (WalletNotFoundException) { return NotFound(new { error = new { code = "WITHDRAWAL_NOT_FOUND", message = "Không tìm thấy yêu cầu rút tiền." } }); }
        catch (WalletValidationException exception) { return UnprocessableEntity(new { error = new { code = exception.Code, message = exception.Message } }); }
        catch (WalletConflictException exception) { return Conflict(new { error = new { code = exception.Code, message = exception.Message } }); }
    }
}

public sealed record AdjustmentRequest(decimal Amount, string Reason, string IdempotencyKey);
public sealed record WithdrawalStatusRequest(Viora.Domain.Entities.WithdrawalStatus Status, string? Reason);
public sealed record WithdrawalFeeSettingsRequest([property: JsonRequired] decimal FeePercent, [property: JsonRequired] long ExpectedVersion);
