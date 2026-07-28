using Microsoft.EntityFrameworkCore;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class GroupCallEntityStateTests
{
    [Fact]
    public void Existing_starter_is_not_inserted_with_new_group_call()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=state_only;Username=test;Password=test")
            .Options;
        using var db = new AppDbContext(options);
        var starter = new User
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            DisplayName = "Starter"
        };

        db.Entry(starter).State = EntityState.Unchanged;
        db.GroupCallSessions.Add(new GroupCallSession
        {
            Id = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            StartedByUserId = starter.Id,
            StartedByUser = starter,
            StartedAt = DateTime.UtcNow
        });

        Assert.Equal(EntityState.Unchanged, db.Entry(starter).State);
        Assert.Equal(EntityState.Added, db.ChangeTracker.Entries<GroupCallSession>().Single().State);
    }
}
