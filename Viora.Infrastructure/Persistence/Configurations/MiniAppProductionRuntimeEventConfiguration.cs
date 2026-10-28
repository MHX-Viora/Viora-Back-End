using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class MiniAppProductionRuntimeEventConfiguration : IEntityTypeConfiguration<MiniAppProductionRuntimeEvent>
{
    public void Configure(EntityTypeBuilder<MiniAppProductionRuntimeEvent> b)
    {
        b.ToTable("MiniAppProductionRuntimeEvents", t => t.HasCheckConstraint("CK_ProductionEvent_Code", "\"Code\" BETWEEN 0 AND 7"));
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => new { x.HostSessionId, x.CreatedAt });
        b.HasIndex(x => new { x.HostSessionId, x.Code }).IsUnique().HasFilter("\"HostSessionId\" IS NOT NULL AND \"Code\" IN (0,1,3,4)");
        b.HasIndex(x => new { x.MiniAppId, x.CreatedAt });
        b.HasOne<MiniApp>().WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppProductionHostSession>().WithMany().HasForeignKey(x => new { x.HostSessionId, x.MiniAppId })
            .HasPrincipalKey(x => new { x.Id, x.MiniAppId }).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in b.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
