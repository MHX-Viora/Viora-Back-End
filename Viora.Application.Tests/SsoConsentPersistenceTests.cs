using Microsoft.EntityFrameworkCore;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class SsoConsentPersistenceTests
{
    [Fact]
    public void PairwiseMappingsAndActiveConsentsAreBoundToOwnerClientAndEnvironment()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Database=unused_model_only;Username=unused").Options);
        var subject = db.Model.FindEntityType("Viora.Domain.Developers.MiniAppPairwiseSubject");
        Assert.NotNull(subject);
        Assert.Contains(subject.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(new[] { "AccountId", "MiniAppId", "Environment" }));
        Assert.Contains(subject.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(new[] { "MiniAppId", "Environment", "Subject" }));
        var consent = db.Model.FindEntityType("Viora.Domain.Developers.MiniAppUserConsent"); Assert.NotNull(consent);
        Assert.True(consent.FindProperty("Version")!.IsConcurrencyToken);
        Assert.Contains(consent.GetIndexes(), x => x.IsUnique && x.GetFilter() == "\"RevokedAt\" IS NULL");
        Assert.Contains(consent.GetForeignKeys(), x => x.PrincipalEntityType.GetTableName() == "OpenIddictAuthorizations" && x.DeleteBehavior == DeleteBehavior.SetNull);
    }
}
