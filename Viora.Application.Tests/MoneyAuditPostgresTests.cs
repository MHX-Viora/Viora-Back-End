using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Viora.Application.Wallets;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Wallets;
using Xunit;

public sealed class MoneyAuditPostgresTests
{
    [PostgresAuditFact]
    public async Task ConcurrentWithdrawalsAndCompletionsCannotSettleTwice()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("VIORA_MONEY_AUDIT_TEST_DB"));
        var database = builder.Database ?? string.Empty;
        if (!database.Contains("test", StringComparison.OrdinalIgnoreCase) && !database.Contains("audit", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use a dedicated test/audit database, never the application database.");
        var schema = "money_audit_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection)) await create.ExecuteNonQueryAsync();
        builder.SearchPath = schema;
        builder.Pooling = false;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(builder.ConnectionString).Options;
        var settings = Options.Create(new WithdrawalOptions { BankAccountEncryptionKey = Convert.ToBase64String(new byte[32]) });
        try
        {
            await using var setup = new AppDbContext(options);
            await setup.Database.ExecuteSqlRawAsync(setup.Database.GenerateCreateScript());
            var account = new Account { Email = "concurrency@example.test" };
            var user = new User { AccountId = account.Id, DisplayName = "Concurrent test" };
            setup.AddRange(account, user, new Wallet { UserId = user.Id, AvailableBalance = 100000 });
            await setup.SaveChangesAsync();
            var setupService = new WithdrawalService(setup, new WalletService(setup), new BankAccountProtector(settings), settings);
            var bank = await setupService.CreateBankAccountAsync(user.Id, new("VCB", "ignored", "1234567890", "TEST USER", true), default);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<WithdrawalResponse?> Withdraw()
            {
                await using var db = new AppDbContext(options);
                var service = new WithdrawalService(db, new WalletService(db), new BankAccountProtector(settings), settings);
                await gate.Task;
                try { return await service.CreateAsync(user.Id, new(60000, bank.Id, Guid.NewGuid().ToString("N")), default); }
                catch (InsufficientWalletBalanceException) { return null; }
            }
            var a = Withdraw(); var b = Withdraw(); gate.SetResult();
            var results = await Task.WhenAll(a, b);
            var w = Assert.Single(results.OfType<WithdrawalResponse>());
            await setupService.ChangeStatusAsync(w.Id, WithdrawalStatus.Processing, null, default, Guid.NewGuid());
            async Task Complete()
            {
                await using var db = new AppDbContext(options);
                var service = new WithdrawalService(db, new WalletService(db), new BankAccountProtector(settings), settings);
                await service.ChangeStatusAsync(w.Id, WithdrawalStatus.Completed, null, default, Guid.NewGuid());
            }
            await Task.WhenAll(Complete(), Complete());
            setup.ChangeTracker.Clear();
            var wallet = await setup.Wallets.SingleAsync();
            Assert.Equal(40000m, wallet.AvailableBalance); Assert.Equal(0m, wallet.HeldBalance);
            Assert.Equal(2, await setup.WalletTransactions.CountAsync());
            Assert.Equal(1, await setup.Withdrawals.CountAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
    private sealed class PostgresAuditFactAttribute : FactAttribute
    {
        public PostgresAuditFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VIORA_MONEY_AUDIT_TEST_DB")))
                Skip = "Requires an isolated PostgreSQL test/audit database via VIORA_MONEY_AUDIT_TEST_DB.";
        }
    }
}
