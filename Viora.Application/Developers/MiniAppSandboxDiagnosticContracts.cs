namespace Viora.Application.Developers;

public sealed record SandboxDiagnosticEvent(string Category,string Code,int? Count=null);
public sealed record SandboxDiagnosticBatch(SandboxDiagnosticEvent[] Events,Guid? TestRunId=null);
public sealed record SandboxDiagnosticDto(Guid Id,Guid SessionId,Guid? TestRunId,string Level,string Category,string Code,
    string EvidenceSource,DateTime CreatedAt,int? Count);
public sealed record SandboxDiagnosticsPage(SandboxDiagnosticDto[] Items,string? NextCursor,int DroppedCount);
public sealed record SandboxDiagnosticIngestion(int AcceptedCount);
