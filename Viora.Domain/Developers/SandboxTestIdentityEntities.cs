using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

// Shared test Account/User; each Auth Bridge launch has an independent owner-bound session.
public sealed class SandboxIdentityTemplate : AuditableEntity
{
    public static readonly Guid DefaultId = Guid.Parse("ac744f84-d1eb-4c32-a8a0-9ac94165be10");
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public string FullName { get; set; } = "ANKT Developer Test";
    public string Nickname { get; set; } = "ankt_sandbox_test";
    public DateOnly Birthdate { get; set; } = new(2000, 1, 1);
}
