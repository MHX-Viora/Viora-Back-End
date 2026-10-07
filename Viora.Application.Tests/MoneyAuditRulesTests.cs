using Microsoft.Extensions.Options;
using Viora.Application.Wallets;
using Viora.Domain.Entities;
using Viora.Infrastructure.Wallets;
using Xunit;

public sealed class MoneyAuditRulesTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"expectedVersion\":1}")]
    [InlineData("{\"feePercent\":10}")]
    public void FeeSettingsMustExplicitlyIncludeRateAndRevision(string json) =>
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<viora_BE.Controllers.Admin.WithdrawalFeeSettingsRequest>(json,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.5")]
    [InlineData("50000.01")]
    [InlineData("9007199254740992")]
    public void InvalidVndIsRejected(string text) => Assert.Throws<WalletValidationException>(() =>
        WalletFinancialRules.RequirePositiveAmount(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact] public void NetPreservesConfiguredFee() => Assert.Equal(495000m, WithdrawalRules.CalculateNetAmount(500000, 5000));
    [Fact] public void FractionalFeeIsRejected() => Assert.Throws<WalletValidationException>(() => WithdrawalRules.CalculateNetAmount(500000, 0.5m));
    [Fact] public void PendingCanBeRejected() => WithdrawalRules.RequireTransition(WithdrawalStatus.Pending, WithdrawalStatus.Rejected);
    [Fact] public void CompletedCannotSettleAgain() => Assert.Throws<WalletConflictException>(() => WithdrawalRules.RequireTransition(WithdrawalStatus.Completed, WithdrawalStatus.Completed));
    [Fact] public void RejectionNeedsReason() => Assert.Throws<WalletValidationException>(() => WithdrawalRules.RequireFailureReason(WithdrawalStatus.Rejected, " "));
    [Fact] public void ReasonsFitDatabase() => Assert.Throws<WalletValidationException>(() => WithdrawalRules.RequireFailureReason(WithdrawalStatus.Rejected, new string('x', 501)));
    [Fact] public void InsufficientFundsAreRejected() => Assert.Throws<InsufficientWalletBalanceException>(() => WalletFinancialRules.RequireSufficientBalance(49999, 50000));
    [Fact] public void BankEncryptionRoundTrip()
    {
        var protector = new BankAccountProtector(Options.Create(new WithdrawalOptions { BankAccountEncryptionKey = Convert.ToBase64String(new byte[32]) }));
        var encrypted = protector.Protect("00123456789");
        Assert.DoesNotContain("00123456789", encrypted);
        Assert.Equal("00123456789", protector.Unprotect(encrypted));
    }
    [Fact] public void WebhookMustHaveAuthenticSignature()
    {
        const string json = "{\"orderCode\":123,\"amount\":50000,\"code\":\"00\"}";
        var signature = PayOsSignature.CreateWebhookSignature(json, "test-key");
        Assert.True(PayOsSignature.VerifyWebhookSignature(json, signature, "test-key"));
        Assert.False(PayOsSignature.VerifyWebhookSignature(json.Replace("50000", "60000"), signature, "test-key"));
    }
}
