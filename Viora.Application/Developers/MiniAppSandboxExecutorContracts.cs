namespace Viora.Application.Developers;

// Internal executor contracts. Never expose through Developer BFF or host/SDK bridge.
public sealed record SandboxExecutorCheck(string Key,string Challenge,DateTime ExpiresAt);
public sealed record SandboxExecutorJob(Guid RunId,Guid SessionId,string AppId,string ConfigurationFingerprint,string WebsiteUrl,
    string PartnerOrigin,string HostOrigin,string PortalOrigin,DateTime DeadlineAt,SandboxExecutorCheck[] Checks);
public sealed record SandboxExecutorReport(string Challenge,string Status);
public sealed record SandboxExecutorLaunch(string Challenge,string BrowserNonce);
