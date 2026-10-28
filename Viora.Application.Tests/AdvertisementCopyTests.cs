using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Advertisements;
using Viora.Domain.Entities;
using Viora.Infrastructure.Advertisements;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class AdvertisementCopyTests
{
    [Fact]
    public void OptionalCopyTrimsAndValidatesLimits()
    {
        Assert.Null(AdvertisementRules.NormalizeCopy("  ", 120));
        Assert.Equal("Custom title", AdvertisementRules.NormalizeCopy(" Custom title ", 120));
        Assert.Equal(new string('a', 120), AdvertisementRules.NormalizeCopy(new string('a', 120), 120));
        Assert.Throws<AdvertisementValidationException>(() => AdvertisementRules.NormalizeCopy(new string('a', 121), 120));
        Assert.Throws<AdvertisementValidationException>(() => AdvertisementRules.NormalizeCopy(new string('a', 241), 240));
    }

    [Fact]
    public async Task DraftCopyPersistsUpdatesAndClearsWithoutChangingPost()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var user = new User { DisplayName = "Advertiser", AccountStyle = AccountStyle.Business, Account = new Account { Email = "advertiser@example.test" } };
        var post = new Post { User = user, Content = "Original content", Status = PostStatus.Published, Visibility = PostVisibility.Public };
        db.Add(post); await db.SaveChangesAsync();
        var service = new AdvertisementService(db, null!, Options.Create(new AdvertisementOptions()));
        var request = new CreateAdvertisementRequest(post.Id, AdvertisementObjective.Awareness, null, AdvertisementCtaType.LearnMore,
            AdvertisementTargetingMode.Automatic, null, null, null, null, 50000, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), " Headline ", " Description ");
        var created = await service.CreateAsync(user.Id, request, default);
        Assert.Equal("Headline", created.Headline); Assert.Equal("Description", created.Description);
        db.ChangeTracker.Clear();
        var saved = await db.Advertisements.SingleAsync(); Assert.Equal("Headline", saved.Headline);
        var updated = await service.CreateAsync(user.Id, request with { Headline = "Revised", Description = " " }, default);
        Assert.Equal(created.Id, updated.Id); Assert.Equal("Revised", updated.Headline); Assert.Null(updated.Description);
        var cleared = await service.CreateAsync(user.Id, request with { Headline = null, Description = null }, default);
        Assert.Null(cleared.Headline); Assert.Null(cleared.Description);
        Assert.Equal("Original content", (await db.Posts.SingleAsync()).Content);
    }
}
