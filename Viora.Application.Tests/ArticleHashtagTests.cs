using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Articles;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Persistence.Repositories;
using Xunit;

public sealed class ArticleHashtagTests
{
    [Fact]
    public async Task SharedTagsPersistThroughCreateEditAndClearWithoutDuplicates()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var owner = new User { DisplayName = "Reporter", AccountStyle = AccountStyle.Journalist, Account = new Account { Email = "article-tags@example.test" } };
        var existing = new Hashtag { Name = "congnghe", PostCount = 1 };
        db.Add(new PostHashtag { Post = new Post { User = owner, PostType = PostType.ShortVideo }, Hashtag = existing });
        await db.SaveChangesAsync();
        var repository = new ArticleRepository(db);
        var create = new CreateArticleHandler(repository, new EfUnitOfWork(db), new CreateArticleValidator());
        var article = await create.Handle(new CreateArticleCommand(owner.Id, "Report", PostVisibility.Public,
            [new(0, ArticleBlockType.Text, "Full report", null, null, null)], ["#CongNghe", "congnghe", "##GiaoDuc"]), default);
        Assert.Equal(new[] { "congnghe", "giaoduc" }, article.Hashtags.Select(tag => tag.Name));
        Assert.Equal(existing.Id, article.Hashtags.Single(tag => tag.Name == "congnghe").Id);
        Assert.Equal(2, await db.Hashtags.CountAsync());
        Assert.Equal(2, (await db.Hashtags.SingleAsync(tag => tag.Name == "congnghe")).PostCount);
        var update = new UpdateArticleHandler(repository, new EfUnitOfWork(db), new UpdateArticleValidator());
        var block = article.Blocks.Single();
        var request = new UpdateArticleCommand(owner.Id, article.Id, "Edited report", PostVisibility.Public,
            [new(block.Id, 0, block.Type, "Updated body", null, null, null)], ["congnghe", "suckhoe"]);
        var edited = await update.Handle(request, default);
        Assert.True(edited.IsSuccess);
        Assert.Equal(new[] { "congnghe", "suckhoe" }, edited.Value!.Hashtags.Select(tag => tag.Name));
        Assert.Equal(0, (await db.Hashtags.SingleAsync(tag => tag.Name == "giaoduc")).PostCount);
        await update.Handle(request, default);
        Assert.Equal(2, await db.PostHashtags.CountAsync(tag => tag.PostId == article.Id));
        Assert.Equal(1, (await db.Hashtags.SingleAsync(tag => tag.Name == "suckhoe")).PostCount);
        var rejected = await update.Handle(request with { UserId = Guid.NewGuid(), Hashtags = [] }, default);
        Assert.False(rejected.IsSuccess);
        var preserved = await update.Handle(request with { Hashtags = null }, default);
        Assert.Equal(2, preserved.Value!.Hashtags.Count);
        var cleared = await update.Handle(request with { Hashtags = [] }, default);
        Assert.Empty(cleared.Value!.Hashtags);
        Assert.Equal(1, (await db.Hashtags.SingleAsync(tag => tag.Name == "congnghe")).PostCount);
        Assert.Equal(0, (await db.Hashtags.SingleAsync(tag => tag.Name == "suckhoe")).PostCount);
        Assert.Equal("Edited report", cleared.Value.Title);
    }

    [Theory]
    [InlineData("#")]
    [InlineData(" ")]
    public void EmptyTopicNamesAreRejected(string name)
    {
        var command = new CreateArticleCommand(Guid.NewGuid(), "Report", PostVisibility.Public,
            [new(0, ArticleBlockType.Text, "Body", null, null, null)], [name]);
        Assert.False(new CreateArticleValidator().Validate(command).IsValid);
    }
}
