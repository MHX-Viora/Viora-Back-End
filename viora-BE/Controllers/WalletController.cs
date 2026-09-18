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
public sealed class WalletController(IWalletService walletService, IPaymentService paymentService, IWithdrawalService withdrawalService) : ControllerBase
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

    [HttpGet("bank-accounts")]
    public async Task<ActionResult<IReadOnlyList<BankAccountResponse>>> BankAccounts(CancellationToken cancellationToken) =>
        TryUserId(out var userId)
            ? Ok(await withdrawalService.GetBankAccountsAsync(userId, cancellationToken))
            : Unauthorized();

    [HttpPost("bank-accounts")]
    public async Task<ActionResult<BankAccountResponse>> CreateBankAccount(BankAccountBody body, CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        try
        {
            var result = await withdrawalService.CreateBankAccountAsync(userId,
                new(body.BankCode, body.BankName, body.AccountNumber, body.AccountHolderName, body.IsDefault), cancellationToken);
            return CreatedAtAction(nameof(BankAccounts), result);
        }
        catch (WalletValidationException exception) { return ProblemResult(422, exception.Code, exception.Message); }
        catch (WalletConflictException exception) { return ProblemResult(409, exception.Code, exception.Message); }
    }

    [HttpGet("withdrawals/quote")]
    public ActionResult<WithdrawalQuoteResponse> WithdrawalQuote([FromQuery] decimal amount)
    {
        try { return Ok(withdrawalService.Quote(amount)); }
        catch (WalletValidationException exception) { return ProblemResult(422, exception.Code, exception.Message); }
    }

    [HttpPost("withdrawals")]
    public async Task<ActionResult<WithdrawalResponse>> CreateWithdrawal(WithdrawalBody body, CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        try
        {
            var result = await withdrawalService.CreateAsync(userId, new(body.Amount, body.BankAccountId, body.IdempotencyKey), cancellationToken);
            return CreatedAtAction(nameof(GetWithdrawal), new { id = result.Id }, result);
        }
        catch (InsufficientWalletBalanceException exception) { return ProblemResult(422, exception.Code, exception.Message); }
        catch (WalletValidationException exception) { return ProblemResult(422, exception.Code, exception.Message); }
        catch (WalletConflictException exception) { return ProblemResult(409, exception.Code, exception.Message); }
    }

    [HttpGet("withdrawals")]
    public async Task<ActionResult<WithdrawalPage>> Withdrawals([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        TryUserId(out var userId)
            ? Ok(await withdrawalService.GetPageAsync(userId, page, pageSize, cancellationToken))
            : Unauthorized();

    [HttpGet("withdrawals/{id:guid}")]
    public async Task<ActionResult<WithdrawalResponse>> GetWithdrawal(Guid id, CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        var result = await withdrawalService.GetAsync(userId, id, cancellationToken);
        return result is null ? ProblemResult(404, "WITHDRAWAL_NOT_FOUND", "Không tìm thấy yêu cầu rút tiền.") : Ok(result);
    }

    [HttpPost("withdrawals/{id:guid}/cancel")]
    public async Task<ActionResult<WithdrawalResponse>> CancelWithdrawal(Guid id, CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        try { return Ok(await withdrawalService.CancelAsync(userId, id, cancellationToken)); }
        catch (WalletNotFoundException) { return ProblemResult(404, "WITHDRAWAL_NOT_FOUND", "Không tìm thấy yêu cầu rút tiền."); }
        catch (WalletConflictException exception) { return ProblemResult(409, exception.Code, exception.Message); }
    }

    private bool TryUserId(out Guid userId) => Guid.TryParse(User.FindFirstValue("user_id"), out userId);
    private static ObjectResult ProblemResult(int status, string code, string message) =>
        new(new { error = new { code, message } }) { StatusCode = status };
}

public sealed record DepositBody(
    [Range(typeof(decimal), "0.01", "9999999999999999")] decimal Amount,
    [Required, Url] string ReturnUrl,
    [Required, Url] string CancelUrl,
    [Required, StringLength(140, MinimumLength = 8)] string IdempotencyKey);

public sealed record BankAccountBody(
    [Required, StringLength(30)] string BankCode,
    [Required, StringLength(120)] string BankName,
    [Required, StringLength(25, MinimumLength = 6)] string AccountNumber,
    [Required, StringLength(120)] string AccountHolderName,
    bool IsDefault);

public sealed record WithdrawalBody(
    [Range(typeof(decimal), "0.01", "9999999999999999")] decimal Amount,
    Guid BankAccountId,
    [Required, StringLength(140, MinimumLength = 8)] string IdempotencyKey);
