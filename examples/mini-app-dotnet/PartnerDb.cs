using Microsoft.EntityFrameworkCore;

public sealed class PartnerDb(DbContextOptions<PartnerDb> options) : DbContext(options)
{
    public DbSet<PartnerUser> Users => Set<PartnerUser>();
    public DbSet<PartnerIdentity> Identities => Set<PartnerIdentity>();
    public DbSet<PartnerTransaction> Transactions => Set<PartnerTransaction>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<PartnerUser>().HasIndex(user => user.Username).IsUnique();
        model.Entity<PartnerIdentity>().HasKey(identity => new { identity.Provider, identity.Subject });
        model.Entity<PartnerIdentity>().HasIndex(identity => new { identity.Provider, identity.UserId }).IsUnique();
        model.Entity<PartnerIdentity>().HasOne<PartnerUser>().WithMany().HasForeignKey(identity => identity.UserId);
        model.Entity<PartnerTransaction>().HasKey(transaction => transaction.StateHash);
    }
}
public sealed class PartnerUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? PasswordHash { get; set; }
}
public sealed class PartnerIdentity
{
    public string Provider { get; set; } = "ANKT";
    public string Subject { get; set; } = "";
    public Guid UserId { get; set; }
}
public sealed class PartnerTransaction
{
    public string StateHash { get; set; } = "";
    public string BrowserHash { get; set; } = "";
    public string ProtectedVerifier { get; set; } = "";
    public string Intent { get; set; } = "login";
    public Guid? UserId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
public sealed class PartnerLinkConflict(string message) : Exception(message);
public sealed class PartnerIdentityService(PartnerDb db)
{
    public async Task<Guid> ResolveAsync(string subject, string? displayName, string intent, Guid? existingUserId, CancellationToken cancellationToken)
    {
        if (!subject.StartsWith("sub_", StringComparison.Ordinal) || subject.Length > 64) throw new PartnerLinkConflict("Invalid ANKT subject");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var linked = await db.Identities.SingleOrDefaultAsync(identity => identity.Provider == "ANKT" && identity.Subject == subject, cancellationToken);
        Guid userId;
        if (intent == "link")
        {
            if (existingUserId is null || !await db.Users.AnyAsync(user => user.Id == existingUserId, cancellationToken)) throw new PartnerLinkConflict("Authenticate the existing account first");
            if (linked is not null && linked.UserId != existingUserId) throw new PartnerLinkConflict("This ANKT identity is linked to another account");
            var own = await db.Identities.SingleOrDefaultAsync(identity => identity.Provider == "ANKT" && identity.UserId == existingUserId, cancellationToken);
            if (own is not null && own.Subject != subject) throw new PartnerLinkConflict("This account is linked to another ANKT identity");
            userId = existingUserId.Value;
        }
        else if (intent == "login")
        {
            if (linked is not null) userId = linked.UserId;
            else
            {
                var user = new PartnerUser { Username = "ankt_" + Guid.NewGuid().ToString("N"), DisplayName = (displayName ?? "ANKT user")[..Math.Min((displayName ?? "ANKT user").Length, 120)] };
                db.Users.Add(user); userId = user.Id;
            }
        }
        else throw new PartnerLinkConflict("Invalid linking intent");
        if (linked is null) db.Identities.Add(new PartnerIdentity { Subject = subject, UserId = userId });
        try { await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateException) { throw new PartnerLinkConflict("Account linking changed concurrently; retry after signing in again"); }
        return userId;
    }
    public async Task UnlinkAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleAsync(user => user.Id == userId, cancellationToken);
        if (user.PasswordHash is null) throw new PartnerLinkConflict("Verify another login method before unlinking");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Identities.Where(identity => identity.Provider == "ANKT" && identity.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Transactions.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
