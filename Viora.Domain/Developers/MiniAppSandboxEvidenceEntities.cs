using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

// Persisted ordinals: append only; never reorder or reuse values in these catalogs.
public enum SandboxTestRunStatus { Queued, Running, Completed, Failed, Cancelled }
public enum SandboxCheckStatus { Passed, Failed, Warning, Skipped, NotImplemented }
public enum SandboxEvidenceSource { Server, Host, Sdk, ServerProtocol, TrustedBrowser }
public enum SandboxDiagnosticLevel { Info, Warning, Error }
public enum SandboxDiagnosticCategory { Runtime, Sso, Bridge, Configuration, Network, Permission }
public enum SandboxCheckKey
{
    Logo, Name, Category, Description, Website, Domain, SupportEmail, Callbacks,
    PrivacyPolicy, TermsOfService, Environment, SsoClient, Discovery, Runtime, Bridge, Consent, Token, UserInfo
}
public enum SandboxMessageCode
{
    SdkInitialized, RuntimeReady, RuntimeError, SsoSucceeded, SsoFailed, BridgeReady, BridgeError,
    NetworkError, PermissionDenied, SessionCreated, SessionClosed, SessionExpired, ConfigurationChanged,
    RunQueued, RunStarted, RunCompleted, RunFailed, RunCancelled, CheckPending, CheckPassed,
    CheckFailed, CheckNotApplicable, MissingEvidence, ExecutorUnavailable
}
public enum SandboxProtocolStage { Authorize, Token, UserInfo }
public enum SandboxProtocolOutcome { Authorized, Denied, TokenIssued, UserInfoValidated, Failed }

public sealed class MiniAppSandboxTestRun : CreatedEntity
{
    public Guid SandboxSessionId { get; set; }
    public MiniAppOAuthEnvironment Environment { get; set; } = MiniAppOAuthEnvironment.Sandbox;
    public string ConfigurationFingerprint { get; set; } = "";
    public SandboxTestRunStatus Status { get; set; }
    public SandboxMessageCode SummaryCode { get; set; } = SandboxMessageCode.RunQueued;
    public DateTime DeadlineAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class MiniAppSandboxTestResult : CreatedEntity
{
    public Guid TestRunId { get; set; }
    public SandboxCheckKey TestKey { get; set; }
    public SandboxCheckStatus Status { get; set; } = SandboxCheckStatus.Skipped;
    public SandboxMessageCode MessageCode { get; set; } = SandboxMessageCode.CheckPending;
    public SandboxEvidenceSource EvidenceSource { get; set; }
    public DateTime? EvidenceAt { get; set; }
    public int DurationMs { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

// App/owner and snapshot resolve through the session/run, never caller fields.
// No arbitrary messages, console output, URLs or credential-bearing strings.
public sealed class MiniAppSandboxDiagnostic : CreatedEntity
{
    public Guid SandboxSessionId { get; set; }
    public Guid? TestRunId { get; set; }
    public SandboxDiagnosticLevel Level { get; set; }
    public SandboxDiagnosticCategory Category { get; set; }
    public SandboxMessageCode Code { get; set; }
    public SandboxEvidenceSource EvidenceSource { get; set; }
    public int? Count { get; set; }
}

// Internal OpenIddict row IDs only; protocol secrets are never persisted here.
public sealed class MiniAppSandboxProtocolEvidence : CreatedEntity
{
    public Guid TestRunId { get; set; }
    public SandboxCheckKey TestKey { get; set; }
    public string EventKey { get; set; } = "";
    public SandboxProtocolStage Stage { get; set; }
    public SandboxProtocolOutcome Outcome { get; set; }
    public string? AuthorizationId { get; set; }
    public string? TokenId { get; set; }
}
