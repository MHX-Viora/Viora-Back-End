using System.Text.RegularExpressions;
using Viora.Application.Developers;
using Viora.Domain.Developers;

namespace Viora.Infrastructure.Developers;

public static class MiniAppSandboxDiagnosticCatalog
{
    private static readonly Dictionary<string,(SandboxDiagnosticCategory Category,SandboxMessageCode Code,SandboxDiagnosticLevel Level)> Observations=new(StringComparer.Ordinal)
    {
        ["SDK_INITIALIZED"]=(SandboxDiagnosticCategory.Runtime,SandboxMessageCode.SdkInitialized,SandboxDiagnosticLevel.Info),
        ["RUNTIME_READY"]=(SandboxDiagnosticCategory.Runtime,SandboxMessageCode.RuntimeReady,SandboxDiagnosticLevel.Info),
        ["RUNTIME_ERROR"]=(SandboxDiagnosticCategory.Runtime,SandboxMessageCode.RuntimeError,SandboxDiagnosticLevel.Error),
        ["SSO_SUCCEEDED"]=(SandboxDiagnosticCategory.Sso,SandboxMessageCode.SsoSucceeded,SandboxDiagnosticLevel.Info),
        ["SSO_FAILED"]=(SandboxDiagnosticCategory.Sso,SandboxMessageCode.SsoFailed,SandboxDiagnosticLevel.Error),
        ["BRIDGE_READY"]=(SandboxDiagnosticCategory.Bridge,SandboxMessageCode.BridgeReady,SandboxDiagnosticLevel.Info),
        ["BRIDGE_ERROR"]=(SandboxDiagnosticCategory.Bridge,SandboxMessageCode.BridgeError,SandboxDiagnosticLevel.Error),
        ["NETWORK_ERROR"]=(SandboxDiagnosticCategory.Network,SandboxMessageCode.NetworkError,SandboxDiagnosticLevel.Error),
        ["PERMISSION_DENIED"]=(SandboxDiagnosticCategory.Permission,SandboxMessageCode.PermissionDenied,SandboxDiagnosticLevel.Warning),
    };
    public static MiniAppSandboxDiagnostic Observation(SandboxDiagnosticEvent value)
    {
        if(value is null || value.Code is null || !Observations.TryGetValue(value.Code,out var entry) || value.Category!=Category(entry.Category) || value.Count is <0 or >1000000)
            throw MiniAppInput.Invalid("Sự kiện Sandbox không thuộc catalog hoặc vượt giới hạn.");
        return new(){Category=entry.Category,Code=entry.Code,Level=entry.Level,Count=value.Count};
    }
    public static SandboxDiagnosticCategory? Filter(string? category)
    {
        if(category==null)return null;
        foreach(var value in Enum.GetValues<SandboxDiagnosticCategory>())if(Category(value)==category)return value;
        throw MiniAppInput.Invalid("Danh mục diagnostic không hợp lệ.");
    }
    private static string Category(SandboxDiagnosticCategory value)=>value==SandboxDiagnosticCategory.Sso?"SSO":value.ToString();
    public static string Code(SandboxMessageCode code)=>Regex.Replace(code.ToString(),"([a-z])([A-Z])","$1_$2").ToUpperInvariant();
    public static string Source(SandboxEvidenceSource source)=>source switch
        {
            SandboxEvidenceSource.Host=>"HostReported",SandboxEvidenceSource.Sdk=>"SdkReported",SandboxEvidenceSource.Server=>"Server",
            SandboxEvidenceSource.ServerProtocol=>"ServerProtocol",SandboxEvidenceSource.TrustedBrowser=>"TrustedBrowser",_=>throw new InvalidOperationException()
        };
    public static SandboxDiagnosticDto Dto(MiniAppSandboxDiagnostic value)=>new(value.Id,value.SandboxSessionId,value.TestRunId,value.Level.ToString(),Category(value.Category),
        Code(value.Code),Source(value.EvidenceSource),DateTime.SpecifyKind(value.CreatedAt,DateTimeKind.Utc),value.Count);
}
