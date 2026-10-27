using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Viora.Application.Developers;
using Viora.Domain.Developers;

namespace Viora.Infrastructure.Developers;

public static class MiniAppReviewEvidencePolicy
{
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };
    public static MiniAppReviewEvidenceAttestation Read(MiniAppReviewRequest review, MiniAppVersion version)
    {
        try
        {
            var snapshot = MiniAppVersionSnapshotPolicy.Read(version.SnapshotJson, version.SnapshotHash).Snapshot;
            if (review.MiniAppVersionId != version.Id || review.MiniAppId != version.MiniAppId || version.State == MiniAppVersionState.Draft ||
                Encoding.UTF8.GetByteCount(review.EvidenceAttestationJson) > 32768) throw Invalid();
            using var document = JsonDocument.Parse(review.EvidenceAttestationJson, new JsonDocumentOptions { MaxDepth = 8 });
            Unique(document.RootElement);
            var evidence = JsonSerializer.Deserialize<MiniAppReviewEvidenceAttestation>(review.EvidenceAttestationJson, Json) ?? throw Invalid();
            if (evidence.SchemaVersion is not (1 or 2) || evidence.SchemaVersion == 2 && snapshot.SsoEnabled || evidence.VersionId != version.Id || evidence.SnapshotHash != version.SnapshotHash ||
                evidence.SandboxRunId == Guid.Empty || evidence.ConfigurationFingerprint is not { Length: 64 } ||
                evidence.ConfigurationFingerprint.Any(c => !Uri.IsHexDigit(c)) || evidence.EvaluatedAt < review.SubmittedAt.AddMinutes(-5) ||
                evidence.EvaluatedAt > review.SubmittedAt.AddSeconds(1) || evidence.Checks?.Length != Enum.GetValues<SandboxCheckKey>().Length) throw Invalid();
            var messages = Enum.GetValues<SandboxMessageCode>().Select(MiniAppSandboxDiagnosticCatalog.Code).ToHashSet(StringComparer.Ordinal);
            foreach (var key in Enum.GetValues<SandboxCheckKey>())
            {
                var wire = char.ToLowerInvariant(key.ToString()[0]) + key.ToString()[1..];
                var checks = evidence.Checks.Where(x => x != null && x.Key == wire).ToArray(); if (checks.Length != 1) throw Invalid();
                var check = checks[0];
                var applicable = evidence.SchemaVersion == 2 ? MiniAppBridgeReviewEvidence.Applicable(key) : snapshot.SsoEnabled || key is not (SandboxCheckKey.Callbacks or SandboxCheckKey.SsoClient or SandboxCheckKey.Discovery
                    or SandboxCheckKey.Consent or SandboxCheckKey.Token or SandboxCheckKey.UserInfo);
                var source = evidence.SchemaVersion == 2 ? MiniAppBridgeReviewEvidence.Protocol(key) ? "ServerProtocol" : "Server" :
                    key is SandboxCheckKey.Runtime or SandboxCheckKey.Bridge ? "TrustedBrowser" : key >= SandboxCheckKey.Consent ? "ServerProtocol" : "Server";
                if (check.Applicable != applicable || !messages.Contains(check.MessageCode) ||
                    (applicable ? check.Status != "Passed" || check.EvidenceSource != source || check.EvidenceAt == null ||
                        check.EvidenceAt < review.SubmittedAt.AddMinutes(-5) || check.EvidenceAt > review.SubmittedAt.AddSeconds(1)
                    : check.Status != "Skipped" || check.EvidenceSource != "Server")) throw Invalid();
            }
            return evidence;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NullReferenceException) { throw Invalid(); }
    }
    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject()) { if (!keys.Add(property.Name)) throw Invalid(); Unique(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Unique(item);
    }
    public static DeveloperAuthException Invalid() => new(409, "REVIEW_EVIDENCE_INVALID", "Bằng chứng đã niêm phong không hợp lệ.");
}
