using Microsoft.EntityFrameworkCore;
public sealed class PartnerIdentityDb(DbContextOptions<PartnerIdentityDb> options) : DbContext(options)
{
    public DbSet<PartnerIdentity> Identities => Set<PartnerIdentity>();
    protected override void OnModelCreating(ModelBuilder model) { model.Entity<PartnerIdentity>().HasKey(x => new { x.Issuer, x.Subject }); }
}
public sealed class PartnerIdentity
{
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public Guid UserId { get; set; } = Guid.NewGuid();
}
