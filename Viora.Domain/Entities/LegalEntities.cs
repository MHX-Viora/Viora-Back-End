namespace Viora.Domain.Entities;

public sealed class LegalDocument
{
    public Guid Id { get; set; }
    public LegalDocumentType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string Content { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "vi";
    public string Version { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class UserLegalAcceptance
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid LegalDocumentId { get; set; }
    public string Version { get; set; } = string.Empty;
    public DateTime AcceptedAt { get; set; }
    public string? AppVersion { get; set; }
    public short? DeviceType { get; set; }
    public string? IpAddress { get; set; }
    public LegalDocument LegalDocument { get; set; } = null!;
    public User User { get; set; } = null!;
}
