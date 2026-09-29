using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Xunit;

namespace Viora.Application.Tests.Live;

public sealed class LiveGiftCoinLedgerSchemaTests
{
    [Fact]
    public void CoinOnlyLedgerConstraintAllowsZeroFiatAmount()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var amountConstraint = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(WalletTransaction))!
            .GetCheckConstraints().Single(x => x.Name == "CK_WalletTransactions_Amount");
        Assert.Contains("\"Amount\" = 0", amountConstraint.Sql, StringComparison.Ordinal);
        Assert.Contains("\"CoinAmount\" IS NOT NULL", amountConstraint.Sql, StringComparison.Ordinal);
        Assert.Contains("\"CoinAmount\" <> 0", amountConstraint.Sql, StringComparison.Ordinal);
    }
}
