using Viora.Application.Wallets;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Wallets;

public sealed class WithdrawalRulesTests
{
    [Theory]
    [InlineData(100_000, 5_000, 95_000)]
    [InlineData(50_000, 0, 50_000)]
    public void CalculateNetAmount_subtracts_the_server_fee(decimal amount, decimal fee, decimal expected)
    {
        Assert.Equal(expected, WithdrawalRules.CalculateNetAmount(amount, fee));
    }

    [Fact]
    public void CalculateNetAmount_rejects_a_fee_that_consumes_the_request()
    {
        var exception = Assert.Throws<WalletValidationException>(
            () => WithdrawalRules.CalculateNetAmount(5_000m, 5_000m));

        Assert.Equal("INVALID_WITHDRAWAL_FEE", exception.Code);
    }

    [Theory]
    [InlineData(WithdrawalStatus.Pending, WithdrawalStatus.Processing)]
    [InlineData(WithdrawalStatus.Pending, WithdrawalStatus.Cancelled)]
    [InlineData(WithdrawalStatus.Processing, WithdrawalStatus.Completed)]
    [InlineData(WithdrawalStatus.Processing, WithdrawalStatus.Failed)]
    [InlineData(WithdrawalStatus.Processing, WithdrawalStatus.Rejected)]
    public void RequireTransition_accepts_supported_lifecycle_changes(
        WithdrawalStatus current,
        WithdrawalStatus next)
    {
        WithdrawalRules.RequireTransition(current, next);
    }

    [Theory]
    [InlineData(WithdrawalStatus.Completed, WithdrawalStatus.Processing)]
    [InlineData(WithdrawalStatus.Cancelled, WithdrawalStatus.Completed)]
    [InlineData(WithdrawalStatus.Pending, WithdrawalStatus.Completed)]
    public void RequireTransition_rejects_unsafe_lifecycle_changes(
        WithdrawalStatus current,
        WithdrawalStatus next)
    {
        var exception = Assert.Throws<WalletConflictException>(
            () => WithdrawalRules.RequireTransition(current, next));

        Assert.Equal("INVALID_WITHDRAWAL_TRANSITION", exception.Code);
    }

    [Theory]
    [InlineData(WithdrawalStatus.Failed)]
    [InlineData(WithdrawalStatus.Rejected)]
    public void RequireFailureReason_rejects_missing_reason(WithdrawalStatus status)
    {
        var exception = Assert.Throws<WalletValidationException>(
            () => WithdrawalRules.RequireFailureReason(status, " "));

        Assert.Equal("WITHDRAWAL_REASON_REQUIRED", exception.Code);
    }
}
