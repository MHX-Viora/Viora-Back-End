using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Utilities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Utilities;
using Xunit;

public sealed class FeaturedBrandTests
{
    private static SaveFeaturedBrand Input(string name = "Brand", int order = 0, bool active = true, long version = 0) =>
        new(name, "https://images.example.test/logo.webp", order, active, version);

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://example.test/logo.png")]
    [InlineData("https://user:pass@example.test/logo.png")]
    [InlineData("//example.test/logo.png")]
    public void RejectsUnsafeLogo(string logo) => Assert.Throws<ArgumentException>(() => FeaturedBrandValidation.Validate(Input() with { LogoUrl = logo }));

    [Fact]
    public void RejectsInvalidNameOrderAndVersion()
    {
        Assert.Throws<ArgumentException>(() => FeaturedBrandValidation.Validate(Input(" ")));
        Assert.Throws<ArgumentException>(() => FeaturedBrandValidation.Validate(Input(new string('a', 161))));
        Assert.Throws<ArgumentException>(() => FeaturedBrandValidation.Validate(Input(order: -1)));
        Assert.Throws<ArgumentException>(() => FeaturedBrandValidation.Validate(Input(version: -1)));
    }

    [Fact]
    public async Task StoresOrdersFiltersAndChecksVersions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(); var store = new FeaturedBrandRepository(db);
        var second = await store.SaveAsync(null, Input("Second", 20), default);
        var first = await store.SaveAsync(null, Input("First", 10), default);
        await store.SaveAsync(null, Input("Hidden", 0, false), default);
        Assert.Equal(new[] { first.Id, second.Id }, (await store.ListAsync(true, default)).Select(x => x.Id));
        Assert.Equal(3, (await store.ListAsync(false, default)).Count);
        var updated = await store.SaveAsync(first.Id, Input("Edited", 10, version: first.Version), default);
        Assert.Equal(first.Version + 1, updated.Version);
        await Assert.ThrowsAsync<FeaturedBrandConflictException>(() => store.SaveAsync(first.Id, Input(version: first.Version), default));
        await Assert.ThrowsAsync<FeaturedBrandConflictException>(() => store.DeleteAsync(first.Id, first.Version, default));
        Assert.True(await store.DeleteAsync(first.Id, updated.Version, default));
        Assert.False(await store.DeleteAsync(first.Id, updated.Version, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.SaveAsync(first.Id, Input(version: updated.Version), default));
    }
}
