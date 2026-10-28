namespace Viora.Application.Developers;

public sealed record SandboxReadinessCheck(string Key,bool Applicable,string Status,string MessageCode,
    string EvidenceSource,DateTime? EvidenceAt);
public sealed record SandboxReadinessDto(string Environment,string ConfigurationFingerprint,bool ConfigurationReady,
    bool ReadyForSandbox,bool ReadyForReview,DateTime EvaluatedAt,SandboxReadinessCheck[] Checks,
    string[] BlockingIssues,string[] Warnings);
