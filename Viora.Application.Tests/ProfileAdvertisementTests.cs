using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Posts;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Persistence.Repositories;
using Xunit;

public sealed class ProfileAdvertisementTests
{
    [Theory]
    [InlineData(PostType.Post)]
    [InlineData(PostType.ShortVideo)]
    public async Task ProfileCampaignProjectionPreservesActivePresentation(PostType postType)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var owner = new User { DisplayName = "Owner", Account = new Account { Email = "profile-ad@example.test", Status = AccountStatus.Active } };
        var post = new Post { User = owner, PostType = postType, Content = "Original content", Status = PostStatus.Published, Visibility = PostVisibility.Public, ReactionCount = 42 };
        var ad = new Advertisement { Post = post, Advertiser = owner, Status = AdvertisementStatus.Active, Placement = postType == PostType.ShortVideo ? AdvertisementPlacement.Reels : AdvertisementPlacement.Feed, CtaType = AdvertisementCtaType.BuyNow, DestinationUrl = "https://example.test/offer", Headline = "Offer", Description = "Details", TotalBudget = 50000, ReservedAmount = 50000, StartAt = DateTime.UtcNow.AddHours(-1), EndAt = DateTime.UtcNow.AddDays(1) };
        db.Add(ad);
        await db.SaveChangesAsync();
        async Task<PostAdvertisementResponse?> Lookup(Guid viewer) =>
            (await PostAdvertisementProjection.LoadAsync(db, [post.Id], viewer, default)).GetValueOrDefault(post.Id);
        var own = await Lookup(owner.Id);
        Assert.Equal(ad.Id, own?.Id);
        Assert.True(own!.IsPreview);
        Assert.Equal(42, (await db.Posts.SingleAsync()).ReactionCount);
        Assert.Equal("Original content", (await db.Posts.SingleAsync()).Content);
        var visitor = await Lookup(Guid.NewGuid());
        Assert.False(visitor!.IsPreview);
        Assert.Equal(ad.DestinationUrl, visitor.DestinationUrl);
        Assert.Equal("Offer", visitor.Headline);
        Assert.Equal("Details", visitor.Description);
        Assert.Empty(await PostAdvertisementProjection.LoadAsync(db, [Guid.NewGuid()], owner.Id, default));
        foreach (var status in new[] { AdvertisementStatus.Paused, AdvertisementStatus.Cancelled, AdvertisementStatus.Completed, AdvertisementStatus.Draft })
        {
            ad.Status = status;
            await db.SaveChangesAsync();
            Assert.Null(await Lookup(owner.Id));
        }
        ad.Status = AdvertisementStatus.Active;
        ad.StartAt = DateTime.UtcNow.AddHours(1);
        await db.SaveChangesAsync();
        Assert.Null(await Lookup(owner.Id));
        ad.StartAt = DateTime.UtcNow.AddDays(-2);
        ad.EndAt = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        Assert.Null(await Lookup(owner.Id));
        ad.EndAt = DateTime.UtcNow.AddDays(1);
        ad.ReservedAmount = 0;
        await db.SaveChangesAsync();
        Assert.Null(await Lookup(owner.Id));
        ad.ReservedAmount = 50000;
        ad.SpentAmount = ad.TotalBudget;
        await db.SaveChangesAsync();
        Assert.Null(await Lookup(owner.Id));
        Assert.Empty(await db.AdvertisementEvents.ToListAsync());
    }
}
