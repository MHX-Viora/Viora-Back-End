using Viora.Domain.Developers;

namespace Viora.Application.Developers;

public sealed record SandboxRunRequest(Guid SessionId,string[]? Checks=null);
public sealed record SandboxRunSummary(Guid Id,Guid SessionId,string Environment,string ConfigurationFingerprint,string Status,
    string SummaryCode,DateTime CreatedAt,DateTime DeadlineAt,DateTime? StartedAt,DateTime? CompletedAt);
public sealed record SandboxCheckResultDto(string Key,string Status,string MessageCode,string EvidenceSource,DateTime? EvidenceAt,int DurationMs);
public sealed record SandboxRunDetail(SandboxRunSummary Run,SandboxCheckResultDto[] Results);
public sealed record SandboxRunPage(SandboxRunSummary[] Items,string? NextCursor);

// Server-owned implementations only. No request or bridge accepts this result contract.
public sealed record SandboxCheckExecution(SandboxCheckStatus Status,SandboxMessageCode MessageCode,SandboxEvidenceSource EvidenceSource,DateTime? EvidenceAt=null);
public interface IMiniAppSandboxCheckExecutor
{
    Task<SandboxCheckExecution> ExecuteAsync(Guid sessionId,SandboxCheckKey key,CancellationToken ct);
}
