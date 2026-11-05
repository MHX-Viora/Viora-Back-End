using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Mentions;
using Viora.Application.Posts;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Persistence.Repositories;
using Xunit;

public sealed class CreatePostImagesTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(16)]
    [InlineData(20)]
    public async Task Images_keep_selection_order_after_save_and_reload(int count)
    {
        await using var fixture = await Fixture.CreateAsync();
        var response = await fixture.Handler.Handle(fixture.Command(count), default);
        var expected = Enumerable.Range(0, count).Select(index => $"https://images.test/{index}.jpg").ToArray();
        Assert.Equal(expected, response.Media.Select(media => media.Url));
        fixture.Db.ChangeTracker.Clear();
        var media = await fixture.Db.PostMedia.AsNoTracking().Where(media => media.PostId == response.Id)
            .OrderBy(media => media.CreatedAt).Select(media => media.MediaUrl).ToListAsync();
        Assert.Equal(expected, media);
        var timestamps = await fixture.Db.PostMedia.OrderBy(media => media.CreatedAt).Select(media => media.CreatedAt).ToListAsync();
        Assert.Equal(count, timestamps.Distinct().Count());
    }

    [Fact]
    public async Task Twenty_one_images_are_rejected_before_any_upload()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => fixture.Handler.Handle(fixture.Command(21), default));
        Assert.Equal(0, fixture.Storage.Calls);
        Assert.Empty(await fixture.Db.Posts.ToListAsync());
    }

    [Fact]
    public async Task Failure_during_upload_does_not_persist_a_partial_post()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Storage.FailAt = 7;
        var error = await Assert.ThrowsAsync<CreatePostException>(() => fixture.Handler.Handle(fixture.Command(20), default));
        Assert.Contains("7/20", error.Message);
        Assert.Equal(7, fixture.Storage.Calls);
        Assert.Empty(await fixture.Db.Posts.ToListAsync());
        Assert.Empty(await fixture.Db.PostMedia.ToListAsync());
    }

    private sealed class Fixture(SqliteConnection connection, AppDbContext db, User user) : IAsyncDisposable, IUnitOfWork, IMentionService
    {
        public AppDbContext Db => db;
        public Guid UserId => user.Id;
        public Storage Storage { get; } = new();
        public CreatePostCommandHandler Handler => new(new PostRepository(db), this, Storage, new CreatePostValidator(), this);
        public CreatePostCommand Command(int count) => new(UserId, "Gallery", PostVisibility.Public, null, null, null, null,
            Enumerable.Range(0, count).Select(index => new CreatePostFile(Stream.Null, $"{index}.jpg", "image/jpeg", 1)).ToList());

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var user = new User { DisplayName = "Gallery owner", Account = new Account { Email = "gallery@example.test", Status = AccountStatus.Active } };
            db.Add(user);
            await db.SaveChangesAsync();
            return new(connection, db, user);
        }

        public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
        {
            await operation(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        public Task<IReadOnlyList<MentionResponse>> CreateAsync(Guid by, Guid target, MentionTargetType type, IReadOnlyList<Guid>? ids, CancellationToken token) => Task.FromResult<IReadOnlyList<MentionResponse>>([]);
        public Task<IReadOnlyList<MentionSearchResponse>> SearchAsync(Guid userId, string? keyword, CancellationToken token) => throw new NotSupportedException();
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class Storage : IMediaStorage
    {
        public int Calls { get; private set; }
        public int? FailAt { get; set; }
        public Task<UploadedMedia> UploadPostImageAsync(Guid userId, CreatePostFile file, CancellationToken token)
        {
            if (++Calls == FailAt) throw new CreatePostException("MEDIA_UPLOAD_FAILED", "Upload failed");
            return Task.FromResult(new UploadedMedia($"https://images.test/{file.FileName}", null));
        }
        public Task<UploadedMedia> UploadReelVideoAsync(Guid userId, CreatePostFile file, CancellationToken token) => throw new NotSupportedException();
        public Task<UploadedChatAttachment> UploadChatAttachmentAsync(Guid userId, CreatePostFile file, CancellationToken token) => throw new NotSupportedException();
        public Task<UploadedMedia> UploadGroupAvatarAsync(Guid userId, CreatePostFile file, CancellationToken token) => throw new NotSupportedException();
    }
}
