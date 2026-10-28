using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppSandboxPersistenceTests
{
    [Fact]
    public void SharedModelBoundsSandboxSessionsAndOneUseGrantsWithoutRawCredentials()
    {
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Database=unused_design_only;Username=unused").Options);
        var session=db.Model.FindEntityType(typeof(MiniAppSandboxSession))!;
        var grant=db.Model.FindEntityType(typeof(MiniAppSandboxLaunchGrant))!;
        Assert.NotNull(session); Assert.NotNull(grant);
        Assert.True(session.FindProperty(nameof(MiniAppSandboxSession.Version))!.IsConcurrencyToken);
        Assert.True(grant.GetIndexes().Single(x=>x.Properties.Any(p=>p.Name==nameof(MiniAppSandboxLaunchGrant.GrantHash))).IsUnique);
        Assert.All(session.GetForeignKeys(),fk=>Assert.Equal(DeleteBehavior.Restrict,fk.DeleteBehavior));
        Assert.All(grant.GetForeignKeys(),fk=>Assert.Equal(DeleteBehavior.Restrict,fk.DeleteBehavior));
        Assert.DoesNotContain(session.GetProperties().Concat(grant.GetProperties()),p=>p.Name is "ClientSecret" or "AccessToken" or "Token" or "Grant" or "Cookie");
        var sql=db.Database.GenerateCreateScript();
        Assert.Contains("CK_MiniAppSandboxSessions_Environment",sql);
        Assert.Contains("CK_MiniAppSandboxSessions_Actor",sql);
        Assert.Contains("CK_MiniAppSandboxLaunchGrants_Expiry",sql);
    }
}
