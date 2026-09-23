using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Advertisements;
using Viora.Domain.Entities;
using Viora.Infrastructure.Advertisements;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Wallets;
using Xunit;

namespace Viora.Application.Tests.Advertisements;

public sealed class AdvertisementDeliveryIntegrationTests
{
    [Theory]
    [InlineData(PostType.Post, AdvertisementPlacement.Feed)]
    [InlineData(PostType.ShortVideo, AdvertisementPlacement.Reels)]
    [InlineData(PostType.Article, AdvertisementPlacement.News)]
    public async Task Active_personal_advertiser_with_held_budget_is_delivered_in_its_placement(
        PostType postType, AdvertisementPlacement placement)
    {
        await using var scope = await DeliveryScope.CreateAsync(postType);

        var result = await scope.Service.GetDeliveryAsync(scope.ViewerId, placement, 3, default);

        Assert.Equal(scope.AdvertisementId, Assert.Single(result.Items).Id);
        Assert.Equal(50_000m, result.Items[0].ReservedAmount);
        Assert.Equal(0m, result.Items[0].SpentAmount);
    }

    [Theory]
    [InlineData(AdvertisementStatus.Pending)]
    [InlineData(AdvertisementStatus.Paused)]
    [InlineData(AdvertisementStatus.Cancelled)]
    [InlineData(AdvertisementStatus.Rejected)]
    public async Task Ineligible_status_is_not_delivered(AdvertisementStatus status)
    {
        await using var scope = await DeliveryScope.CreateAsync(PostType.Post, status);
        Assert.Empty((await scope.Service.GetDeliveryAsync(scope.ViewerId, AdvertisementPlacement.Feed, 3, default)).Items);
    }

    [Fact]
    public async Task Approved_ad_starts_at_its_utc_schedule_and_wrong_placement_stays_empty()
    {
        await using var scope = await DeliveryScope.CreateAsync(PostType.Post, AdvertisementStatus.Approved, startsInFuture: true);
        Assert.Empty((await scope.Service.GetDeliveryAsync(scope.ViewerId, AdvertisementPlacement.Feed, 3, default)).Items);
        var ad = await scope.Db.Advertisements.SingleAsync();
        ad.StartAt = DateTime.UtcNow.AddMinutes(-1);
        await scope.Db.SaveChangesAsync();

        Assert.Empty((await scope.Service.GetDeliveryAsync(scope.ViewerId, AdvertisementPlacement.News, 3, default)).Items);
        Assert.Equal(scope.AdvertisementId, Assert.Single((await scope.Service.GetDeliveryAsync(scope.ViewerId, AdvertisementPlacement.Feed, 3, default)).Items).Id);
        Assert.Equal(AdvertisementStatus.Active, (await scope.Db.Advertisements.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Pause_resume_and_cancel_control_delivery_and_refund_unused_budget()
    {
        await using var scope = await DeliveryScope.CreateAsync(PostType.Post);
        await scope.Service.PauseAsync(scope.AdvertiserId, scope.AdvertisementId, default);
        Assert.Empty((await scope.Service.GetDeliveryAsync(scope.ViewerId, AdvertisementPlacement.Feed, 3, default)).Items);
        await Assert.ThrowsAsync<AdvertisementConflictException>(() => scope.Service.RecordEventAsync(
            scope.ViewerId, scope.AdvertisementId, AdvertisementEventType.Impression, new("paused-view"), default));

        await scope.Service.ResumeAsync(scope.AdvertiserId, scope.AdvertisementId, default);
        Assert.Single((await scope.Service.GetDeliveryAsync(scope.ViewerId, AdvertisementPlacement.Feed, 3, default)).Items);

        await scope.Service.CancelAsync(scope.AdvertiserId, scope.AdvertisementId, default);
        Assert.Empty((await scope.Service.GetDeliveryAsync(scope.ViewerId, AdvertisementPlacement.Feed, 3, default)).Items);
        var wallet = await scope.Db.Wallets.AsNoTracking().SingleAsync();
        Assert.Equal(50_000m, wallet.AvailableBalance);
        Assert.Equal(0m, wallet.HeldBalance);
    }

    [Fact]
    public async Task View_and_click_update_metrics_and_spend_only_once_per_event()
    {
        await using var scope = await DeliveryScope.CreateAsync(PostType.Post);
        await scope.Service.RecordEventAsync(scope.ViewerId, scope.AdvertisementId, AdvertisementEventType.Impression, new("visible-once"), default);
        var duplicate = await scope.Service.RecordEventAsync(scope.ViewerId, scope.AdvertisementId, AdvertisementEventType.Impression, new("visible-once"), default);
        await scope.Service.RecordEventAsync(scope.ViewerId, scope.AdvertisementId, AdvertisementEventType.Click, new("clicked-once"), default);

        var ad = await scope.Service.GetAsync(scope.AdvertiserId, scope.AdvertisementId, default);
        Assert.True(duplicate.IsDuplicate);
        Assert.Equal(1, ad.Impressions);
        Assert.Equal(1, ad.Clicks);
        Assert.Equal(100m, ad.ClickThroughRate);
        Assert.Equal(600m, ad.SpentAmount);
        Assert.Equal(49_400m, ad.ReservedAmount);
        Assert.Equal(49_400m, (await scope.Db.Wallets.AsNoTracking().SingleAsync()).HeldBalance);
    }

    [Fact]
    public async Task Expired_campaign_stops_delivery_and_releases_remaining_budget()
    {
        await using var scope = await DeliveryScope.CreateAsync(PostType.Post);
        var ad = await scope.Db.Advertisements.SingleAsync();
        ad.EndAt = DateTime.UtcNow.AddMinutes(-1);
        await scope.Db.SaveChangesAsync();

        Assert.Empty((await scope.Service.GetDeliveryAsync(scope.ViewerId, AdvertisementPlacement.Feed, 3, default)).Items);
        Assert.Equal(AdvertisementStatus.Completed, ad.Status);
        Assert.Equal(0m, ad.ReservedAmount);
        Assert.Equal(50_000m, (await scope.Db.Wallets.AsNoTracking().SingleAsync()).AvailableBalance);
    }

    [Fact]
    public async Task Exhausted_campaign_and_hidden_post_are_not_delivered()
    {
        await using var scope = await DeliveryScope.CreateAsync(PostType.Post);
        var post = await scope.Db.Posts.SingleAsync();
        post.Status = PostStatus.Hidden;
        await scope.Db.SaveChangesAsync();
        Assert.Empty((await scope.Service.GetDeliveryAsync(scope.ViewerId, AdvertisementPlacement.Feed, 3, default)).Items);

        post.Status = PostStatus.Published;
        var ad = await scope.Db.Advertisements.SingleAsync();
        ad.SpentAmount = 50_000m;
        ad.ReservedAmount = 0m;
        await scope.Db.SaveChangesAsync();
        Assert.Empty((await scope.Service.GetDeliveryAsync(scope.ViewerId, AdvertisementPlacement.Feed, 3, default)).Items);
    }

    [Fact]
    public async Task Daily_budget_cap_blocks_delivery_even_when_campaign_has_held_funds()
    {
        await using var scope = await DeliveryScope.CreateAsync(PostType.Post);
        var ad = await scope.Db.Advertisements.SingleAsync();
        ad.DailyBudget = 100m;
        ad.SpentAmount = 100m;
        ad.ReservedAmount = 49_900m;
        (await scope.Db.Wallets.SingleAsync()).HeldBalance = 49_900m;
        scope.Db.AdvertisementEvents.Add(new AdvertisementEvent
        {
            AdvertisementId = ad.Id, ViewerId = scope.ViewerId, Type = AdvertisementEventType.Click,
            ClientEventId = "earlier-charge", ChargeAmount = 100m
        });
        await scope.Db.SaveChangesAsync();

        Assert.Empty((await scope.Service.GetDeliveryAsync(scope.ViewerId, AdvertisementPlacement.Feed, 3, default)).Items);
        Assert.Equal(49_900m, ad.ReservedAmount);
    }

    private sealed class DeliveryScope : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public AppDbContext Db { get; }
        public AdvertisementService Service { get; }
        public Guid ViewerId { get; }
        public Guid AdvertiserId { get; }
        public Guid AdvertisementId { get; }

        private DeliveryScope(SqliteConnection connection, AppDbContext db, Guid viewerId, Guid advertiserId, Guid advertisementId)
        {
            this.connection = connection;
            Db = db;
            ViewerId = viewerId;
            AdvertiserId = advertiserId;
            AdvertisementId = advertisementId;
            Service = new AdvertisementService(db, new WalletService(db), Options.Create(new AdvertisementOptions()));
        }

        public static async Task<DeliveryScope> CreateAsync(PostType postType, AdvertisementStatus status = AdvertisementStatus.Active, bool startsInFuture = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateFunction("char_length", (string value) => value.Length);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var advertiserAccount = new Account { Email = "advertiser@example.com" };
            var viewerAccount = new Account { Email = "viewer@example.com" };
            var advertiser = new User { AccountId = advertiserAccount.Id, DisplayName = "Advertiser", AccountStyle = AccountStyle.Personal };
            var viewer = new User { AccountId = viewerAccount.Id, DisplayName = "Viewer" };
            var post = new Post { UserId = advertiser.Id, PostType = postType, Content = "Sponsored content" };
            var now = DateTime.UtcNow;
            var ad = new Advertisement
            {
                PostId = post.Id, AdvertiserId = advertiser.Id,
                Placement = postType switch { PostType.ShortVideo => AdvertisementPlacement.Reels, PostType.Article => AdvertisementPlacement.News, _ => AdvertisementPlacement.Feed },
                Status = status, StartAt = now.AddMinutes(startsInFuture ? 10 : -10), EndAt = now.AddDays(1),
                TotalBudget = 50_000m, ReservedAmount = 50_000m
            };
            db.Accounts.AddRange(advertiserAccount, viewerAccount);
            db.Users.AddRange(advertiser, viewer);
            db.Wallets.Add(new Wallet { UserId = advertiser.Id, HeldBalance = 50_000m });
            db.Posts.Add(post);
            db.Advertisements.Add(ad);
            await db.SaveChangesAsync();
            return new DeliveryScope(connection, db, viewer.Id, advertiser.Id, ad.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
