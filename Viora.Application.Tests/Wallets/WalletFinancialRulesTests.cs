using Viora.Application.Wallets;
using Xunit;

namespace Viora.Application.Tests.Wallets;

public sealed class WalletFinancialRulesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RequirePositiveAmount_rejects_non_positive_values(decimal amount)
    {
        var exception = Assert.Throws<WalletValidationException>(
            () => WalletFinancialRules.RequirePositiveAmount(amount));

        Assert.Equal("INVALID_AMOUNT", exception.Code);
    }

    [Fact]
    public void RequireSufficientBalance_reports_the_missing_amount()
    {
        var exception = Assert.Throws<InsufficientWalletBalanceException>(
            () => WalletFinancialRules.RequireSufficientBalance(200_000m, 500_000m));

        Assert.Equal(300_000m, exception.Shortfall);
    }

    [Fact]
    public void BuildPayOsSignature_uses_the_documented_sorted_fields()
    {
        var signature = PayOsSignature.CreatePaymentRequestSignature(
            10_000,
            "https://app.test/cancel",
            "NAP VI 123",
            123,
            "https://app.test/return",
            "checksum");

        Assert.Equal(
            "be847ab0ee9b70d681e570e29a4ed980be4eaeaece74be07f424de7bbe6d8831",
            signature);
    }

    [Fact]
    public void VerifyWebhookSignature_rejects_a_modified_payload()
    {
        const string json = "{\"orderCode\":123,\"amount\":3000,\"description\":\"ANKT123\"}";
        var valid = PayOsSignature.CreateWebhookSignature(json, "checksum");

        Assert.True(PayOsSignature.VerifyWebhookSignature(json, valid, "checksum"));
        Assert.False(PayOsSignature.VerifyWebhookSignature(
            "{\"orderCode\":123,\"amount\":3001,\"description\":\"ANKT123\"}",
            valid,
            "checksum"));
    }

    [Fact]
    public void ResolveQrPayload_falls_back_to_checkout_url()
    {
        Assert.Equal(
            "https://pay.payos.vn/web/checkout",
            PaymentCheckout.ResolveQrPayload("   ", "  https://pay.payos.vn/web/checkout  "));
    }

    [Fact]
    public void ResolveQrPayload_returns_null_when_provider_data_is_blank()
    {
        Assert.Null(PaymentCheckout.ResolveQrPayload(" ", "\t"));
    }
}
