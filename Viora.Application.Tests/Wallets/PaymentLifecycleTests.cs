using Viora.Application.Wallets;
using Viora.Domain.Entities;
using System.Text.Json;
using Xunit;

namespace Viora.Application.Tests.Wallets;

public sealed class PaymentLifecycleTests
{
    [Fact]
    public void CalculateExpiry_uses_the_server_creation_time()
    {
        var createdAt = new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc);

        Assert.Equal(createdAt.AddMinutes(15), PaymentLifecycle.CalculateExpiry(createdAt));
    }

    [Theory]
    [InlineData(PaymentStatus.Pending, true)]
    [InlineData(PaymentStatus.Expired, true)]
    [InlineData(PaymentStatus.Paid, false)]
    [InlineData(PaymentStatus.Failed, false)]
    [InlineData(PaymentStatus.Cancelled, false)]
    public void CanComplete_only_accepts_uncredited_provider_confirmations(PaymentStatus status, bool expected)
    {
        Assert.Equal(expected, PaymentLifecycle.CanComplete(status));
    }

    [Theory]
    [InlineData(PaymentStatus.Pending, true)]
    [InlineData(PaymentStatus.Paid, false)]
    [InlineData(PaymentStatus.Failed, false)]
    [InlineData(PaymentStatus.Cancelled, false)]
    [InlineData(PaymentStatus.Expired, false)]
    public void CanCancel_only_accepts_an_open_payment(PaymentStatus status, bool expected)
    {
        Assert.Equal(expected, PaymentLifecycle.CanCancel(status));
    }

    [Fact]
    public void DepositIdempotencyKey_is_stable_for_every_delivery()
    {
        var paymentId = Guid.Parse("19a3d4c4-98bf-4a56-8fca-9cb04a34fe8b");

        Assert.Equal("deposit:19a3d4c4-98bf-4a56-8fca-9cb04a34fe8b", PaymentLifecycle.DepositIdempotencyKey(paymentId));
    }

    [Theory]
    [InlineData(PaymentStatus.Pending, true)]
    [InlineData(PaymentStatus.Paid, false)]
    [InlineData(PaymentStatus.Failed, false)]
    [InlineData(PaymentStatus.Cancelled, false)]
    [InlineData(PaymentStatus.Expired, false)]
    public void Only_pending_payment_expires_when_the_deadline_passes(PaymentStatus status, bool expected)
    {
        var now = new DateTime(2026, 9, 19, 1, 15, 1, DateTimeKind.Utc);

        Assert.Equal(expected, PaymentLifecycle.ShouldExpire(status, now.AddSeconds(-1), now));
    }

    [Theory]
    [InlineData("PAID", PaymentStatus.Paid)]
    [InlineData("CANCELLED", PaymentStatus.Cancelled)]
    [InlineData("PENDING", PaymentStatus.Pending)]
    [InlineData("PROCESSING", PaymentStatus.Pending)]
    public void Provider_status_is_normalized_for_reconciliation(string providerStatus, PaymentStatus expected)
    {
        Assert.Equal(expected, PaymentLifecycle.FromProviderStatus(providerStatus));
    }

    [Fact]
    public void Provider_expiry_uses_unix_seconds_from_the_server_deadline()
    {
        var expiresAt = new DateTime(2026, 9, 19, 1, 15, 0, DateTimeKind.Utc);

        Assert.Equal(1_789_780_500, PaymentLifecycle.ProviderExpiryTimestamp(expiresAt));
    }

    [Theory]
    [InlineData("FAILED", PaymentStatus.Failed)]
    [InlineData("EXPIRED", PaymentStatus.Expired)]
    public void Terminal_provider_statuses_are_recognized(string providerStatus, PaymentStatus expected) =>
        Assert.Equal(expected, PaymentLifecycle.FromProviderStatus(providerStatus));

    [Fact]
    public void Cancellation_requires_a_signed_confirmation_for_the_expected_order()
    {
        const string data = "{\"orderCode\":123,\"status\":\"CANCELLED\"}";
        var signature = PayOsSignature.CreateWebhookSignature(data, "checksum");
        using var response = JsonDocument.Parse($"{{\"code\":\"00\",\"data\":{data},\"signature\":\"{signature}\"}}");
        Assert.True(PaymentLifecycle.IsConfirmedCancellation(response.RootElement, 123, "checksum"));
        Assert.False(PaymentLifecycle.IsConfirmedCancellation(response.RootElement, 124, "checksum"));
        Assert.False(PaymentLifecycle.IsConfirmedCancellation(response.RootElement, 123, "wrong-key"));
        using var malformed = JsonDocument.Parse("[]");
        Assert.False(PaymentLifecycle.IsConfirmedCancellation(malformed.RootElement, 123, "checksum"));
    }
}
