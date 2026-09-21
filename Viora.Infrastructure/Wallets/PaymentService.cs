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
        var createdAt = DateTime.UtcNow;
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
            Status = PaymentStatus.Pending,
            ExpiresAt = PaymentLifecycle.CalculateExpiry(createdAt)
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
        var description = PaymentCheckout.CreateDescription(payment.ProviderOrderCode);
        var payload = new
        {
            orderCode = payment.ProviderOrderCode,
            amount,
            description,
            cancelUrl,
            returnUrl,
            expiredAt = PaymentLifecycle.ProviderExpiryTimestamp(payment.ExpiresAt),
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
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "payOS create-payment failed with HTTP {StatusCode} for order {OrderCode}.",
                (int)response.StatusCode,
                payment.ProviderOrderCode);
            throw new WalletConflictException("PAYOS_UNAVAILABLE", "Không thể tạo phiên thanh toán payOS. Vui lòng thử lại sau.");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String || code.GetString() != "00" ||
            !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            logger.LogWarning(
                "payOS returned response code {ProviderCode} without payment data for order {OrderCode}.",
                code.ValueKind == JsonValueKind.String ? code.GetString() : "missing",
                payment.ProviderOrderCode);
            throw new WalletConflictException("PAYOS_INVALID_RESPONSE", "Phản hồi payOS không hợp lệ.");
        }
        if (!document.RootElement.TryGetProperty("signature", out var responseSignature) ||
            !PayOsSignature.VerifyWebhookSignature(data.GetRawText(), responseSignature.GetString() ?? string.Empty, checksumKey))
        {
            logger.LogWarning("payOS returned an invalid signature for order {OrderCode}.", payment.ProviderOrderCode);
            throw new WalletConflictException("PAYOS_INVALID_SIGNATURE", "Không thể xác minh phản hồi payOS.");
        }
        payment.ProviderTransactionId = ReadString(data, "paymentLinkId");
        payment.CheckoutUrl = PaymentCheckout.Normalize(ReadString(data, "checkoutUrl"));
        payment.QrCode = PaymentCheckout.ResolveQrPayload(ReadString(data, "qrCode"), payment.CheckoutUrl);
        if (payment.QrCode is null)
        {
            logger.LogWarning("payOS returned blank QR and checkout data for order {OrderCode}.", payment.ProviderOrderCode);
            throw new WalletConflictException("PAYOS_INVALID_RESPONSE", "Phản hồi payOS không chứa dữ liệu thanh toán.");
        }
        var ledger = new WalletTransaction
        {
            WalletId = wallet.Id,
            Type = WalletTransactionType.Deposit,
            Amount = request.Amount,
            BalanceBefore = wallet.AvailableBalance,
            BalanceAfter = wallet.AvailableBalance,
            HeldBefore = wallet.HeldBalance,
            HeldAfter = wallet.HeldBalance,
            ReferenceType = "Payment",
            ReferenceId = payment.Id.ToString("D"),
            Description = "Nạp tiền bằng mã QR",
            Status = WalletTransactionStatus.Pending,
            IdempotencyKey = PaymentLifecycle.DepositIdempotencyKey(payment.Id)
        };
        dbContext.WalletTransactions.Add(ledger);
        payment.LedgerTransactionId = ledger.Id;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(payment);
    }

    public async Task<PaymentResponse?> GetAsync(Guid userId, Guid paymentId, CancellationToken cancellationToken)
    {
        await ReconcileWithProviderAsync(userId, paymentId, cancellationToken);
        await ExpireIfNeededAsync(userId, paymentId, cancellationToken);
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

        if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
            !data.TryGetProperty("code", out var dataCode) || dataCode.ValueKind != JsonValueKind.String || dataCode.GetString() != "00" ||
            !data.TryGetProperty("orderCode", out var orderCodeElement) || !orderCodeElement.TryGetInt64(out var orderCode) ||
            !data.TryGetProperty("amount", out var amountElement) || !amountElement.TryGetDecimal(out var amount)) return;
        var reference = ReadString(data, "reference") ?? ReadString(data, "paymentLinkId") ?? $"order-{orderCode}";
        var exists = await dbContext.Payments.AsNoTracking().AnyAsync(payment => payment.ProviderOrderCode == orderCode, cancellationToken);
        if (!exists)
        {
            logger.LogInformation("Ignoring valid payOS webhook for unknown order {OrderCode}.", orderCode);
            return;
        }
        await walletService.CompleteDepositAsync(orderCode, reference, amount, cancellationToken);
    }

    private async Task ReconcileWithProviderAsync(Guid userId, Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await dbContext.Payments.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == paymentId && item.UserId == userId && item.Status == PaymentStatus.Pending,
            cancellationToken);
        if (payment is null) return;

        var clientId = configuration["PAYOS_CLIENT_ID"] ?? Environment.GetEnvironmentVariable("PAYOS_CLIENT_ID");
        var apiKey = configuration["PAYOS_API_KEY"] ?? Environment.GetEnvironmentVariable("PAYOS_API_KEY");
        var checksumKey = configuration["PAYOS_CHECKSUM_KEY"] ?? Environment.GetEnvironmentVariable("PAYOS_CHECKSUM_KEY");
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(checksumKey)) return;

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, $"v2/payment-requests/{payment.ProviderOrderCode}");
            message.Headers.Add("x-client-id", clientId);
            message.Headers.Add("x-api-key", apiKey);
            using var response = await httpClientFactory.CreateClient("payos").SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode) return;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String || code.GetString() != "00" ||
                !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("signature", out var signature) || signature.ValueKind != JsonValueKind.String ||
                !PayOsSignature.VerifyWebhookSignature(data.GetRawText(), signature.GetString() ?? string.Empty, checksumKey)) return;
            if (!data.TryGetProperty("orderCode", out var orderCodeElement) || !orderCodeElement.TryGetInt64(out var orderCode) || orderCode != payment.ProviderOrderCode ||
                !data.TryGetProperty("status", out var statusElement) || statusElement.ValueKind != JsonValueKind.String) return;

            var providerStatus = PaymentLifecycle.FromProviderStatus(statusElement.GetString());
            if (providerStatus == PaymentStatus.Paid)
            {
                if (!data.TryGetProperty("amount", out var amountElement) || !amountElement.TryGetDecimal(out var amount)) return;
                var reference = ReadString(data, "id") ?? payment.ProviderTransactionId ?? $"order-{orderCode}";
                await walletService.CompleteDepositAsync(orderCode, reference, amount, cancellationToken);
            }
            else if (providerStatus == PaymentStatus.Cancelled)
            {
                await SetCancelledAsync(payment.Id, cancellationToken);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Unable to reconcile payOS order {OrderCode}; keeping its current status.", payment.ProviderOrderCode);
        }
    }

    private async Task SetCancelledAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var payment = await dbContext.Payments
            .FromSqlInterpolated($"SELECT * FROM \"Payments\" WHERE \"Id\" = {paymentId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        if (payment.Status != PaymentStatus.Pending)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        payment.Status = PaymentStatus.Cancelled;
        payment.CancelledAt = DateTime.UtcNow;
        if (payment.LedgerTransactionId is Guid ledgerId)
        {
            var ledger = await dbContext.WalletTransactions.SingleOrDefaultAsync(
                item => item.Id == ledgerId && item.Status == WalletTransactionStatus.Pending,
                cancellationToken);
            if (ledger is not null) ledger.Status = WalletTransactionStatus.Failed;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ExpireIfNeededAsync(Guid userId, Guid paymentId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var payment = await dbContext.Payments
            .FromSqlInterpolated($"SELECT * FROM \"Payments\" WHERE \"Id\" = {paymentId} AND \"UserId\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (payment is null || !PaymentLifecycle.ShouldExpire(payment.Status, payment.ExpiresAt, DateTime.UtcNow))
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        payment.Status = PaymentStatus.Expired;
        if (payment.LedgerTransactionId is Guid ledgerId)
        {
            var ledger = await dbContext.WalletTransactions.SingleOrDefaultAsync(
                item => item.Id == ledgerId && item.Status == WalletTransactionStatus.Pending,
                cancellationToken);
            if (ledger is not null) ledger.Status = WalletTransactionStatus.Failed;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
    private static PaymentResponse Map(Payment payment) => new(payment.Id, payment.Amount, payment.Currency, payment.Status, payment.Provider, payment.ProviderOrderCode, payment.ProviderTransactionId, payment.CheckoutUrl, payment.QrCode, PaymentCheckout.CreateDescription(payment.ProviderOrderCode), payment.LedgerTransactionId, payment.CreatedAt, payment.ExpiresAt, payment.PaidAt);
}
