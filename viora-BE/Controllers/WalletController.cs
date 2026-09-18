using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Wallets;
using Viora.Domain.Entities;

namespace viora_BE.Controllers;

[ApiController]
[Authorize]
[Route("api/wallet")]
public sealed class WalletController(IWalletService walletService, IPaymentService paymentService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WalletResponse>> Get(CancellationToken cancellationToken) =>
        TryUserId(out var userId)
            ? Ok(await walletService.GetOrCreateAsync(userId, cancellationToken))
            : Unauthorized();

    [HttpPost("deposits")]
    public async Task<ActionResult<PaymentResponse>> CreateDeposit(DepositBody body, CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        try
        {
            var result = await paymentService.CreateDepositAsync(userId, new CreateDepositRequest(
                body.Amount, body.ReturnUrl, body.CancelUrl, body.IdempotencyKey), cancellationToken);
            return CreatedAtAction(nameof(GetPayment), new { id = result.Id }, result);
        }
        catch (WalletValidationException exception) { return ProblemResult(422, exception.Code, exception.Message); }
        catch (WalletConflictException exception) { return ProblemResult(409, exception.Code, exception.Message); }
    }

    [HttpGet("payments/{id:guid}")]
    public async Task<ActionResult<PaymentResponse>> GetPayment(Guid id, CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        var result = await paymentService.GetAsync(userId, id, cancellationToken);
        return result is null ? ProblemResult(404, "PAYMENT_NOT_FOUND", "Không tìm thấy payment.") : Ok(result);
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<WalletTransactionPage>> Transactions(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        [FromQuery] WalletTransactionType? type = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        return Ok(await walletService.GetTransactionsAsync(userId, page, pageSize, type, cancellationToken));
    }

    [HttpGet("transactions/{id:guid}")]
    public async Task<ActionResult<WalletTransactionResponse>> Transaction(Guid id, CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        var result = await walletService.GetTransactionAsync(userId, id, cancellationToken);
        return result is null ? ProblemResult(404, "TRANSACTION_NOT_FOUND", "Không tìm thấy giao dịch.") : Ok(result);
    }

    private bool TryUserId(out Guid userId) => Guid.TryParse(User.FindFirstValue("user_id"), out userId);
    private static ObjectResult ProblemResult(int status, string code, string message) =>
        new(new { error = new { code, message } }) { StatusCode = status };
}

public sealed record DepositBody(
    [property: Range(typeof(decimal), "0.01", "9999999999999999")] decimal Amount,
    [property: Required, Url] string ReturnUrl,
    [property: Required, Url] string CancelUrl,
    [property: Required, StringLength(140, MinimumLength = 8)] string IdempotencyKey);
