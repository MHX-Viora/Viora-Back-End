using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Viora.Application.Advertisements;
using Viora.Domain.Entities;
using Viora.Infrastructure.Advertisements;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Wallets;
using Xunit;

public sealed class AdvertisementAutoStartTests
{
    [Theory]
    [InlineData(PostType.Post)] [InlineData(PostType.ShortVideo)] [InlineData(PostType.Article)]
    public async Task PaidAdsRunImmediatelyForEveryPlacementWithoutReview(PostType type)
    {
        await using var f = await Fixture.Create(type);
        var started = await f.Service.SubmitAsync(f.User.Id, f.Ad.Id, default);
        Assert.Equal(AdvertisementStatus.Active, started.Status);
        Assert.Equal(50000, started.ReservedAmount);
        Assert.Null((await f.Db.Advertisements.SingleAsync()).ReviewedBy);
        Assert.Null((await f.Db.Advertisements.SingleAsync()).ReviewedAt);
        var wallet = await f.Db.Wallets.SingleAsync(); Assert.Equal(50000, wallet.AvailableBalance); Assert.Equal(50000, wallet.HeldBalance);
        var repeated = await f.Service.SubmitAsync(f.User.Id, f.Ad.Id, default);
        Assert.Equal(AdvertisementStatus.Active, repeated.Status);
        Assert.Single(await f.Db.WalletTransactions.ToListAsync());
        var cancelled = await f.Service.CancelAsync(f.User.Id, f.Ad.Id, default);
        Assert.Equal(AdvertisementStatus.Cancelled, cancelled.Status);
        wallet = await f.Db.Wallets.SingleAsync(); Assert.Equal(100000, wallet.AvailableBalance); Assert.Equal(0, wallet.HeldBalance);
    }

    [Fact]
    public async Task InsufficientFundsAndNonOwnerNeverActivateOrHold()
    {
        await using var f = await Fixture.Create(balance: 49999);
        await Assert.ThrowsAsync<AdvertisementForbiddenException>(() => f.Service.SubmitAsync(Guid.NewGuid(), f.Ad.Id, default));
        await Assert.ThrowsAsync<AdvertisementInsufficientBalanceException>(() => f.Service.SubmitAsync(f.User.Id, f.Ad.Id, default));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(AdvertisementStatus.Draft, (await f.Db.Advertisements.SingleAsync()).Status);
        var wallet = await f.Db.Wallets.SingleAsync(); Assert.Equal(49999, wallet.AvailableBalance); Assert.Equal(0, wallet.HeldBalance);
        Assert.Empty(await f.Db.WalletTransactions.ToListAsync());
    }

    [Fact]
    public async Task FutureStartSchedulesThenActivatesWithoutAnotherPayment()
    {
        await using var f = await Fixture.Create(future: true);
        Assert.Equal(AdvertisementStatus.Approved, (await f.Service.SubmitAsync(f.User.Id, f.Ad.Id, default)).Status);
        var ad = await f.Db.Advertisements.SingleAsync(); ad.StartAt = DateTime.UtcNow.AddMinutes(-1); await f.Db.SaveChangesAsync();
        await f.Service.SynchronizeLifecycleAsync(default);
        Assert.Equal(AdvertisementStatus.Active, (await f.Db.Advertisements.SingleAsync()).Status);
        Assert.Single(await f.Db.WalletTransactions.ToListAsync());
    }

    [Fact]
    public async Task ExistingFundedPendingAdsStartWithoutAnotherHold()
    {
        await using var f = await Fixture.Create();
        var wallet = new WalletService(f.Db);
        await wallet.HoldAsync(f.User.Id, 50000, "Advertisement", f.Ad.Id.ToString("D"), $"advertisement:{f.Ad.Id:D}:reserve", default);
        var ad = await f.Db.Advertisements.SingleAsync(); ad.Status = AdvertisementStatus.Pending; ad.ReservedAmount = 50000; await f.Db.SaveChangesAsync();
        await f.Service.SynchronizeLifecycleAsync(default);
        Assert.Equal(AdvertisementStatus.Active, ad.Status);
        Assert.Single(await f.Db.WalletTransactions.ToListAsync());
    }

    [Fact]
    public async Task NonPublicPostCannotStartEvenWithEnoughMoney()
    {
        await using var f = await Fixture.Create();
        (await f.Db.Posts.SingleAsync()).Visibility = PostVisibility.Private; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<AdvertisementValidationException>(() => f.Service.SubmitAsync(f.User.Id, f.Ad.Id, default));
        Assert.Empty(await f.Db.WalletTransactions.ToListAsync());
    }

    [Fact]
    public async Task ActivationFailureRollsBackTheWalletHoldAndLedger()
    {
        var failure = new FailActivation();
        await using var f = await Fixture.Create(interceptor: failure);
        failure.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SubmitAsync(f.User.Id, f.Ad.Id, default));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(AdvertisementStatus.Draft, (await f.Db.Advertisements.SingleAsync()).Status);
        var wallet = await f.Db.Wallets.SingleAsync(); Assert.Equal(100000, wallet.AvailableBalance); Assert.Equal(0, wallet.HeldBalance);
        Assert.Empty(await f.Db.WalletTransactions.ToListAsync());
    }

    [Fact]
    public async Task ExpiredLegacyPendingReturnsUnusedMoney()
    {
        await using var f = await Fixture.Create();
        await new WalletService(f.Db).HoldAsync(f.User.Id, 50000, "Advertisement", f.Ad.Id.ToString("D"), $"advertisement:{f.Ad.Id:D}:reserve", default);
        var ad = await f.Db.Advertisements.SingleAsync(); ad.Status = AdvertisementStatus.Pending; ad.ReservedAmount = 50000;
        ad.StartAt = DateTime.UtcNow.AddDays(-2); ad.EndAt = DateTime.UtcNow.AddDays(-1); await f.Db.SaveChangesAsync();
        await f.Service.SynchronizeLifecycleAsync(default);
        Assert.Equal(AdvertisementStatus.Completed, ad.Status);
        var wallet = await f.Db.Wallets.SingleAsync(); Assert.Equal(100000, wallet.AvailableBalance); Assert.Equal(0, wallet.HeldBalance);
    }

    private sealed class FailActivation : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<Advertisement>().Any(entry => entry.State == EntityState.Modified && entry.Entity.Status == AdvertisementStatus.Active))
                throw new InvalidOperationException("Injected activation failure after wallet hold");
            return base.SavingChangesAsync(eventData, result, token);
        }
    }

    private sealed class Fixture(SqliteConnection connection, AppDbContext db, User user, AdvertisementResponse ad, AdvertisementService service) : IAsyncDisposable
    {
        public AppDbContext Db => db; public User User => user; public AdvertisementResponse Ad => ad; public AdvertisementService Service => service;
        public static async Task<Fixture> Create(PostType type = PostType.Post, decimal balance = 100000, bool future = false, SaveChangesInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            var db = new AppDbContext(options.Options); await db.Database.EnsureCreatedAsync();
            var user = new User { DisplayName = "Advertiser", AccountStyle = AccountStyle.Business, Account = new Account { Email = "auto-ad@example.test" } };
            var post = new Post { User = user, Content = "Public content", PostType = type };
            db.Add(post); db.Add(new Wallet { User = user, AvailableBalance = balance }); await db.SaveChangesAsync();
            var service = new AdvertisementService(db, new WalletService(db), Options.Create(new AdvertisementOptions()));
            var start = future ? DateTime.UtcNow.AddDays(1) : DateTime.UtcNow.AddMinutes(-1);
            var ad = await service.CreateAsync(user.Id, new(post.Id, AdvertisementObjective.Awareness, null, AdvertisementCtaType.LearnMore,
                AdvertisementTargetingMode.Automatic, null, null, null, null, 50000, start, start.AddDays(3)), default);
            return new(connection, db, user, ad, service);
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
