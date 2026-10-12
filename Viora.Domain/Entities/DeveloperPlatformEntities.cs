namespace Viora.Domain.Entities;

public sealed class DeveloperApplication : CreatedEntity
{
    public Guid DeveloperId { get; set; }
    public int Version { get; set; }
    public string ConfigurationJson { get; set; } = null!;
    public string Status { get; set; } = "PendingReview";
    public DateTime SubmittedAt { get; set; }
    public string TermsVersion { get; set; } = null!;
    public DateTime TermsAcceptedAt { get; set; }
    public Guid SubmittedByAccountId { get; set; }
    public DateTime EmailVerifiedAt { get; set; }
    public Developer Developer { get; set; } = null!;
    public Account SubmittedByAccount { get; set; } = null!;
}

public sealed class DeveloperReviewHistory : CreatedEntity
{
    public Guid DeveloperId { get; set; }
    public int Version { get; set; }
    public Guid ReviewerAccountId { get; set; }
    public string PreviousStatus { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? Reason { get; set; }
    public string? InternalNotes { get; set; }
    public Developer Developer { get; set; } = null!;
    public Account ReviewerAccount { get; set; } = null!;
}

public sealed class DeveloperEmailVerification : CreatedEntity
{
    public Guid AccountId { get; set; }
    public string Email { get; set; } = null!;
    public string CodeHash { get; set; } = null!;
    public long RequestWindow { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public int Attempts { get; set; }
    public bool DeliverySucceeded { get; set; }
    public Account Account { get; set; } = null!;
}

public sealed class MiniAppTechnicalTest : CreatedEntity
{
    public Guid MiniAppId { get; set; }
    public Guid TestedByAccountId { get; set; }
    public string ConfigurationHash { get; set; } = null!;
    public bool Passed { get; set; }
    public string ResultJson { get; set; } = null!;
    public MiniApp MiniApp { get; set; } = null!;
    public Account TestedByAccount { get; set; } = null!;
}
