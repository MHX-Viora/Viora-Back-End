using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Viora.Application.Wallets;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Wallets;

public sealed class PaymentService(
    AppDbContext dbContext,
    IWalletService walletService,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<PaymentService> logger) : IPaymentService
{
    private const string Provider = "payOS";

    public async Task<PaymentResponse> CreateDepositAsync(Guid userId, CreateDepositRequest request, CancellationToken cancellationToken)
    {
        WalletFinancialRules.RequirePositiveAmount(request.Amount);
        var minAmount = ReadDecimal("Wallet:MinDeposit", 10_000m);
        var maxAmount = ReadDecimal("Wallet:MaxDeposit", 100_000_000m);
        if (request.Amount < minAmount || request.Amount > maxAmount)
            throw new WalletValidationException("DEPOSIT_LIMIT", $"Số tiền nạp phải từ {minAmount:0} đến {maxAmount:0} VND.");
        if (request.Amount != decimal.Truncate(request.Amount) || request.Amount > long.MaxValue)
            throw new WalletValidationException("INVALID_VND_AMOUNT", "Số tiền VND phải là số nguyên.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 140)
            throw new WalletValidationException("INVALID_IDEMPOTENCY_KEY", "Idempotency key không hợp lệ.");

        var existing = await dbContext.Payments.AsNoTracking().SingleOrDefaultAsync(
            payment => payment.UserId == userId && payment.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);
        if (existing is not null) return Map(existing);

        var clientId = RequiredSecret("PAYOS_CLIENT_ID");
        var apiKey = RequiredSecret("PAYOS_API_KEY");
        var checksumKey = RequiredSecret("PAYOS_CHECKSUM_KEY");
        var returnUrl = ResolveUrl("PAYOS_RETURN_URL", request.ReturnUrl);
        var cancelUrl = ResolveUrl("PAYOS_CANCEL_URL", request.CancelUrl);
        var wallet = await walletService.GetOrCreateAsync(userId, cancellationToken);
        var payment = new Payment
        {
            UserId = userId,
            WalletId = wallet.Id,
            Amount = request.Amount,
            Currency = "VND",
            Purpose = "WalletDeposit",
            ReferenceType = "Wallet",
            ReferenceId = wallet.Id.ToString("D"),
            Provider = Provider,
            ProviderOrderCode = checked(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000 + Random.Shared.Next(0, 1000)),
            IdempotencyKey = request.IdempotencyKey,
            Status = PaymentStatus.Pending
        };
        dbContext.Payments.Add(payment);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.Entry(payment).State = EntityState.Detached;
            var duplicate = await dbContext.Payments.AsNoTracking().SingleOrDefaultAsync(
                item => item.UserId == userId && item.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (duplicate is not null) return Map(duplicate);
            throw new WalletConflictException("PAYMENT_ORDER_CONFLICT", "Không thể tạo mã payment duy nhất. Vui lòng thử lại.");
        }

        var amount = decimal.ToInt64(request.Amount);
        var description = $"ANKT {payment.ProviderOrderCode}";
        if (description.Length > 25) description = description[^25..];
        var payload = new
        {
            orderCode = payment.ProviderOrderCode,
            amount,
            description,
            cancelUrl,
            returnUrl,
            signature = PayOsSignature.CreatePaymentRequestSignature(amount, cancelUrl, description, payment.ProviderOrderCode, returnUrl, checksumKey)
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "v2/payment-requests")
        {
            Content = JsonContent.Create(payload)
        };
        message.Headers.Add("x-client-id", clientId);
        message.Headers.Add("x-api-key", apiKey);
        using var response = await httpClientFactory.CreateClient("payos").SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new WalletConflictException("PAYOS_UNAVAILABLE", "Không thể tạo phiên thanh toán payOS.");

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("code", out var code) || code.GetString() != "00" ||
            !document.RootElement.TryGetProperty("data", out var data))
            throw new WalletConflictException("PAYOS_INVALID_RESPONSE", "Phản hồi payOS không hợp lệ.");
        if (!document.RootElement.TryGetProperty("signature", out var responseSignature) ||
            !PayOsSignature.VerifyWebhookSignature(data.GetRawText(), responseSignature.GetString() ?? string.Empty, checksumKey))
            throw new WalletConflictException("PAYOS_INVALID_SIGNATURE", "Không thể xác minh phản hồi payOS.");
        payment.ProviderTransactionId = ReadString(data, "paymentLinkId");
        payment.CheckoutUrl = ReadString(data, "checkoutUrl");
        payment.QrCode = ReadString(data, "qrCode");
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(payment);
    }

    public async Task<PaymentResponse?> GetAsync(Guid userId, Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await dbContext.Payments.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == paymentId && item.UserId == userId, cancellationToken);
        return payment is null ? null : Map(payment);
    }

    public async Task HandlePayOsWebhookAsync(string payload, CancellationToken cancellationToken)
    {
        var checksumKey = RequiredSecret("PAYOS_CHECKSUM_KEY");
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || !root.TryGetProperty("signature", out var signatureElement))
            throw new WalletValidationException("INVALID_WEBHOOK", "Webhook payOS thiếu dữ liệu.");
        var signature = signatureElement.GetString() ?? string.Empty;
        if (!PayOsSignature.VerifyWebhookSignature(data.GetRawText(), signature, checksumKey))
            throw new WalletValidationException("INVALID_WEBHOOK_SIGNATURE", "Chữ ký webhook payOS không hợp lệ.");

        if (!root.TryGetProperty("success", out var success) || !success.GetBoolean() ||
            !data.TryGetProperty("code", out var dataCode) || dataCode.GetString() != "00") return;
        var orderCode = data.GetProperty("orderCode").GetInt64();
        var amount = data.GetProperty("amount").GetDecimal();
        var reference = ReadString(data, "reference") ?? ReadString(data, "paymentLinkId") ?? $"order-{orderCode}";
        var exists = await dbContext.Payments.AsNoTracking().AnyAsync(payment => payment.ProviderOrderCode == orderCode, cancellationToken);
        if (!exists)
        {
            logger.LogInformation("Ignoring valid payOS webhook for unknown order {OrderCode}.", orderCode);
            return;
        }
        await walletService.CompleteDepositAsync(orderCode, reference, amount, $"payos:{orderCode}:{reference}", cancellationToken);
    }

    private decimal ReadDecimal(string key, decimal fallback) => decimal.TryParse(configuration[key], out var value) ? value : fallback;

    private string RequiredSecret(string key)
    {
        var value = configuration[key] ?? Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value)) throw new WalletConflictException("PAYOS_NOT_CONFIGURED", "payOS chưa được cấu hình.");
        return value;
    }

    private string ResolveUrl(string key, string requestValue)
    {
        var value = configuration[key] ?? Environment.GetEnvironmentVariable(key) ?? requestValue;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new WalletValidationException("INVALID_RETURN_URL", "URL thanh toán không hợp lệ.");
        return uri.ToString();
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static PaymentResponse Map(Payment payment) => new(payment.Id, payment.Amount, payment.Currency, payment.Status, payment.Provider, payment.ProviderOrderCode, payment.ProviderTransactionId, payment.CheckoutUrl, payment.QrCode, payment.CreatedAt, payment.PaidAt);
}
