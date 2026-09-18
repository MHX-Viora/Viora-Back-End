using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Viora.Domain.Entities;

namespace Viora.Application.Wallets;

public class WalletValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class InsufficientWalletBalanceException(decimal available, decimal requested)
    : WalletValidationException("INSUFFICIENT_BALANCE", "Số dư khả dụng không đủ.")
{
    public decimal Available { get; } = available;
    public decimal Requested { get; } = requested;
    public decimal Shortfall { get; } = requested - available;
}

public sealed class WalletNotFoundException() : Exception("Không tìm thấy ví.");
public sealed class WalletConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class WalletFinancialRules
{
    public static void RequirePositiveAmount(decimal amount)
    {
        if (amount <= 0) throw new WalletValidationException("INVALID_AMOUNT", "Số tiền phải lớn hơn 0.");
        if (decimal.Round(amount, 2) != amount) throw new WalletValidationException("INVALID_AMOUNT_PRECISION", "Số tiền chỉ được có tối đa 2 chữ số thập phân.");
    }

    public static void RequireSufficientBalance(decimal available, decimal requested)
    {
        RequirePositiveAmount(requested);
        if (available < requested) throw new InsufficientWalletBalanceException(available, requested);
    }
}

public static class WithdrawalRules
{
    public static decimal CalculateNetAmount(decimal amount, decimal fee)
    {
        WalletFinancialRules.RequirePositiveAmount(amount);
        if (fee < 0 || fee >= amount)
            throw new WalletValidationException("INVALID_WITHDRAWAL_FEE", "Phí rút tiền không hợp lệ.");
        return amount - fee;
    }

    public static void RequireTransition(WithdrawalStatus current, WithdrawalStatus next)
    {
        var allowed = (current, next) switch
        {
            (WithdrawalStatus.Pending, WithdrawalStatus.Processing) => true,
            (WithdrawalStatus.Pending, WithdrawalStatus.Cancelled) => true,
            (WithdrawalStatus.Processing, WithdrawalStatus.Completed) => true,
            (WithdrawalStatus.Processing, WithdrawalStatus.Failed) => true,
            (WithdrawalStatus.Processing, WithdrawalStatus.Rejected) => true,
            _ => false
        };

        if (!allowed)
            throw new WalletConflictException("INVALID_WITHDRAWAL_TRANSITION", "Không thể chuyển sang trạng thái rút tiền này.");
    }

    public static void RequireFailureReason(WithdrawalStatus status, string? reason)
    {
        if ((status is WithdrawalStatus.Failed or WithdrawalStatus.Rejected) && string.IsNullOrWhiteSpace(reason))
            throw new WalletValidationException("WITHDRAWAL_REASON_REQUIRED", "Cần nhập lý do khi yêu cầu thất bại hoặc bị từ chối.");
    }
}

public static class PaymentCheckout
{
    public static string? ResolveQrPayload(string? qrCode, string? checkoutUrl) =>
        Normalize(qrCode) ?? Normalize(checkoutUrl);

    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class PayOsSignature
{
    public static string CreatePaymentRequestSignature(
        long amount, string cancelUrl, string description, long orderCode, string returnUrl, string checksumKey) =>
        Hmac($"amount={amount}&cancelUrl={cancelUrl}&description={description}&orderCode={orderCode}&returnUrl={returnUrl}", checksumKey);

    public static string CreateWebhookSignature(string dataJson, string checksumKey)
    {
        using var document = JsonDocument.Parse(dataJson);
        return Hmac(Canonicalize(document.RootElement), checksumKey);
    }

    public static bool VerifyWebhookSignature(string dataJson, string signature, string checksumKey)
    {
        if (string.IsNullOrWhiteSpace(signature)) return false;
        var expected = CreateWebhookSignature(dataJson, checksumKey);
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected), Convert.FromHexString(signature));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Canonicalize(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => string.Join("&", element.EnumerateObject()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={CanonicalValue(property.Value)}")),
        _ => throw new ArgumentException("payOS signature data must be an object.", nameof(element))
    };

    private static string CanonicalValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Array => JsonSerializer.Serialize(value.EnumerateArray().Select(SortedObject).ToArray()),
        JsonValueKind.Object => JsonSerializer.Serialize(SortedObject(value)),
        _ => value.ToString()
    };

    private static SortedDictionary<string, object?> SortedObject(JsonElement element) =>
        new(element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => ToObject(property.Value)), StringComparer.Ordinal);

    private static object? ToObject(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDecimal(),
        JsonValueKind.Object => SortedObject(value),
        JsonValueKind.Array => value.EnumerateArray().Select(ToObject).ToArray(),
        _ => value.GetRawText()
    };

    private static string Hmac(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }
}

public sealed record WalletResponse(Guid Id, decimal AvailableBalance, decimal HeldBalance, long AnktCoinBalance, string Currency, WalletStatus Status);
public sealed record WalletTransactionResponse(Guid Id, WalletTransactionType Type, decimal Amount, decimal BalanceBefore, decimal BalanceAfter, decimal HeldBefore, decimal HeldAfter, string ReferenceType, string ReferenceId, string? Description, WalletTransactionStatus Status, WithdrawalStatus? WithdrawalStatus, DateTime CreatedAt, DateTime? CompletedAt);
public sealed record WalletTransactionPage(IReadOnlyList<WalletTransactionResponse> Data, int Page, int PageSize, int TotalItems, int TotalPages);
public sealed record CreateDepositRequest(decimal Amount, string ReturnUrl, string CancelUrl, string IdempotencyKey);
public sealed record PaymentResponse(Guid Id, decimal Amount, string Currency, PaymentStatus Status, string Provider, long ProviderOrderCode, string? ProviderTransactionId, string? CheckoutUrl, string? QrCode, DateTime CreatedAt, DateTime? PaidAt);
public sealed record BankAccountResponse(Guid Id, string BankCode, string BankName, string AccountNumberMasked, string AccountHolderName, bool IsDefault, DateTime CreatedAt);
public sealed record CreateBankAccountRequest(string BankCode, string BankName, string AccountNumber, string AccountHolderName, bool IsDefault);
public sealed record CreateWithdrawalRequest(decimal Amount, Guid BankAccountId, string IdempotencyKey);
public sealed record WithdrawalResponse(Guid Id, string TransactionCode, decimal Amount, decimal Fee, decimal NetAmount, string Currency, WithdrawalStatus Status, Guid BankAccountId, string BankCode, string BankName, string BankAccountMasked, string BankAccountHolderName, string? FailureReason, DateTime CreatedAt, DateTime UpdatedAt, DateTime? ProcessingAt, DateTime? CompletedAt);
public sealed record WithdrawalQuoteResponse(decimal Amount, decimal Fee, decimal NetAmount, decimal MinimumAmount, decimal MaximumAmount);
public sealed record WithdrawalPage(IReadOnlyList<WithdrawalResponse> Data, int Page, int PageSize, int TotalItems, int TotalPages);

public sealed class WithdrawalOptions
{
    public decimal Fee { get; set; } = 5_000m;
    public decimal MinimumAmount { get; set; } = 50_000m;
    public decimal MaximumAmount { get; set; } = 50_000_000m;
    public string BankAccountEncryptionKey { get; set; } = string.Empty;
}

public interface IBankAccountProtector
{
    string Protect(string accountNumber);
    string Hash(string accountNumber);
}

public interface IWithdrawalService
{
    Task<IReadOnlyList<BankAccountResponse>> GetBankAccountsAsync(Guid userId, CancellationToken cancellationToken);
    Task<BankAccountResponse> CreateBankAccountAsync(Guid userId, CreateBankAccountRequest request, CancellationToken cancellationToken);
    WithdrawalQuoteResponse Quote(decimal amount);
    Task<WithdrawalResponse> CreateAsync(Guid userId, CreateWithdrawalRequest request, CancellationToken cancellationToken);
    Task<WithdrawalResponse?> GetAsync(Guid userId, Guid withdrawalId, CancellationToken cancellationToken);
    Task<WithdrawalPage> GetPageAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);
    Task<WithdrawalPage> GetAdminPageAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<WithdrawalResponse> CancelAsync(Guid userId, Guid withdrawalId, CancellationToken cancellationToken);
    Task<WithdrawalResponse> ChangeStatusAsync(Guid withdrawalId, WithdrawalStatus status, string? reason, CancellationToken cancellationToken);
}

public interface IWalletService
{
    Task<WalletResponse> GetOrCreateAsync(Guid userId, CancellationToken cancellationToken);
    Task<WalletTransactionPage> GetTransactionsAsync(Guid userId, int page, int pageSize, WalletTransactionType? type, CancellationToken cancellationToken);
    Task<WalletTransactionResponse?> GetTransactionAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken);
    Task<WalletTransactionResponse> CompleteDepositAsync(long providerOrderCode, string providerTransactionId, decimal amount, string idempotencyKey, CancellationToken cancellationToken);
    Task<WalletTransactionResponse> HoldAsync(Guid userId, decimal amount, string referenceType, string referenceId, string idempotencyKey, CancellationToken cancellationToken);
    Task<WalletTransactionResponse> ReleaseAsync(Guid userId, decimal amount, string referenceType, string referenceId, string idempotencyKey, CancellationToken cancellationToken);
    Task<WalletTransactionResponse> CaptureAsync(Guid userId, decimal amount, string referenceType, string referenceId, string idempotencyKey, CancellationToken cancellationToken);
    Task<WalletTransactionResponse> RefundAsync(Guid userId, decimal amount, string referenceType, string referenceId, string idempotencyKey, CancellationToken cancellationToken);
    Task<WalletTransactionResponse> AdjustAsync(Guid userId, Guid adminId, decimal amount, string reason, string idempotencyKey, CancellationToken cancellationToken);
}

public interface IPaymentService
{
    Task<PaymentResponse> CreateDepositAsync(Guid userId, CreateDepositRequest request, CancellationToken cancellationToken);
    Task<PaymentResponse?> GetAsync(Guid userId, Guid paymentId, CancellationToken cancellationToken);
    Task HandlePayOsWebhookAsync(string payload, CancellationToken cancellationToken);
}
