using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Persistence.Migrations;
using Xunit;

namespace Viora.Application.Tests.Live;

public sealed class LiveGiftVndSchemaTests
{
    [Fact]
    public void RollbackRefusesToDiscardVndFinancialSnapshots()
    {
        var operations = new AddLiveGiftVndWalletTransfers().DownOperations;
        var guard = Assert.IsType<SqlOperation>(operations[0]).Sql;
        Assert.Contains("\"Currency\" = 'VND'", guard);
        Assert.Contains("\"Metadata\" IS NOT NULL", guard);
        Assert.Contains("RAISE EXCEPTION", guard);
    }

    [Fact]
    public void MigrationAddsVndColumnsWithoutRewritingFinancialHistory()
    {
        var operations = new AddLiveGiftVndWalletTransfers().UpOperations;
        Assert.DoesNotContain(operations, x => x is DropColumnOperation or DropTableOperation
            or DeleteDataOperation or UpdateDataOperation or AlterColumnOperation);
        var sql = Assert.Single(operations.OfType<SqlOperation>()).Sql;
        Assert.Contains("UPDATE \"LiveGifts\"", sql);
        foreach (var table in new[] { "Wallets", "WalletTransactions", "LiveGiftTransactions" })
            Assert.DoesNotContain($"\"{table}\"", sql);
        foreach (var column in new[] { "UnitPrice", "GrossAmount", "NetAmount", "FeeAmount", "ReceiverWalletTransactionId", "GiftName" })
            Assert.True(Assert.Single(operations.OfType<AddColumnOperation>(), x => x.Name == column).IsNullable);
        Assert.Contains("WHEN 1 THEN 50000", sql);
        Assert.Contains("WHEN 2 THEN 200000", sql);
        Assert.Contains("WHEN 3 THEN 500000", sql);
        Assert.Contains("CAST(\"PriceCoin\" AS numeric) * 1000", sql);
    }

    [Fact]
    public void LegacyCoinFieldsRemainWhileVndAmountsAreNullable()
    {
        using var db = CreateContext();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(LiveGiftTransaction))!;
        foreach (var name in new[] { "UnitPriceCoin", "TotalCoin", "PlatformFee", "HostEarning" })
            Assert.NotNull(entity.FindProperty(name));
        foreach (var name in new[] { "UnitPrice", "GrossAmount", "NetAmount", "FeeAmount", "ReceiverWalletTransactionId", "GiftName" })
            Assert.True(entity.FindProperty(name)?.IsNullable, $"{name} must preserve legacy rows through nullable values.");
        Assert.Equal("COIN", entity.FindProperty("Currency")?.GetDefaultValue());
        Assert.Contains(entity.GetIndexes(), x => x.IsUnique && x.Properties.SingleOrDefault()?.Name == "RequestId");
    }

    [Theory]
    [InlineData("COIN", "NULL", "NULL", "NULL", "NULL", "NULL", 0, false)]
    [InlineData("VND", "50000", "100000", "100000", "0", "'receiver'", 2, false)]
    [InlineData("VND", "NULL", "100000", "100000", "0", "'receiver'", 2, true)]
    [InlineData("VND", "50000", "100000", "100000", "0", "NULL", 2, true)]
    [InlineData("VND", "50000", "100000", "99999", "0", "'receiver'", 2, true)]
    [InlineData("VND", "50000", "100000", "99999", "1", "'receiver'", 2, false)]
    [InlineData("VND", "50000", "100000", "100001", "-1", "'receiver'", 2, true)]
    [InlineData("VND", "50000", "100000", "100000", "0", "'receiver'", 1, true)]
    [InlineData("VND", "50000", "100000", "100000", "0", "'receiver'", 0, true)]
    public void ConstraintPreservesCoinRowsAndRejectsIncompleteVndTransfers(
        string currency, string unit, string gross, string net, string fee, string receiver, int quantity, bool rejected)
    {
        using var db = CreateContext();
        var constraint = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(LiveGiftTransaction))!
            .GetCheckConstraints().Single(x => x.Name == "CK_LiveGiftTransactions_VndTransfer");
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE Gifts (Currency TEXT, UnitPrice INTEGER, GrossAmount INTEGER, NetAmount INTEGER,
                FeeAmount INTEGER, ReceiverWalletTransactionId TEXT, WalletTransactionId TEXT,
                Quantity INTEGER, SenderUserId TEXT, HostUserId TEXT, GiftName TEXT, Status INTEGER,
                CHECK ({constraint.Sql}));
            INSERT INTO Gifts VALUES ('{currency}', {unit}, {gross}, {net}, {fee}, {receiver},
                'sender-ledger', {quantity}, 'sender', 'host', 'Firework', 1);
            """;
        if (rejected) Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        else command.ExecuteNonQuery();
    }

    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite("Data Source=:memory:").Options);
}
