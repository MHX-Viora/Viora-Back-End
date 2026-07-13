namespace Viora.Domain.Entities;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public abstract class CreatedEntity : Entity
{
    public DateTime CreatedAt { get; set; }
}

public abstract class AuditableEntity : CreatedEntity
{
    public DateTime UpdatedAt { get; set; }
}
