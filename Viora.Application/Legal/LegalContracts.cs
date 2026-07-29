using Viora.Domain.Entities;

namespace Viora.Application.Legal;

public sealed record LegalDocumentSummaryResponse(
    Guid Id, LegalDocumentType Type, string Title, string? Summary, string LanguageCode,
    string Version, bool IsPublished, DateTime? PublishedAt, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record LegalDocumentResponse(
    Guid Id, LegalDocumentType Type, string Title, string? Summary, string Content,
    string LanguageCode, string Version, bool IsPublished, DateTime? PublishedAt,
    Guid? CreatedBy, Guid? UpdatedBy, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record SaveLegalDocumentRequest(
    LegalDocumentType Type, string Title, string? Summary, string Content,
    string LanguageCode, string Version);

public enum LegalMutationError { NotFound, Invalid, Conflict }
public sealed record LegalMutationResult(bool Success, LegalDocumentResponse? Document, LegalMutationError? Error, string? Message)
{
    public static LegalMutationResult Ok(LegalDocumentResponse? document = null) => new(true, document, null, null);
    public static LegalMutationResult Fail(LegalMutationError error, string message) => new(false, null, error, message);
}

public interface ILegalDocumentRepository
{
    Task<IReadOnlyList<LegalDocumentSummaryResponse>> GetPublishedAsync(CancellationToken token);
    Task<LegalDocumentResponse?> GetPublishedAsync(LegalDocumentType type, CancellationToken token);
    Task<IReadOnlyList<LegalDocumentSummaryResponse>> GetAllAsync(CancellationToken token);
    Task<LegalDocumentResponse?> GetByIdAsync(Guid id, CancellationToken token);
    Task<LegalMutationResult> CreateAsync(Guid adminId, SaveLegalDocumentRequest request, CancellationToken token);
    Task<LegalMutationResult> CreateVersionAsync(Guid adminId, Guid sourceId, SaveLegalDocumentRequest request, CancellationToken token);
    Task<LegalMutationResult> PublishAsync(Guid adminId, Guid id, CancellationToken token);
    Task<LegalMutationResult> DeleteDraftAsync(Guid id, CancellationToken token);
    Task<LegalMutationResult> AcceptAsync(Guid userId, Guid documentId, string version, string? appVersion, short? deviceType, string? ipAddress, CancellationToken token);
}

public static class LegalDocumentRules
{
    public static string? Validate(SaveLegalDocumentRequest request)
    {
        if (!Enum.IsDefined(request.Type)) return "Document type is invalid.";
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 255) return "Title is required and limited to 255 characters.";
        if (string.IsNullOrWhiteSpace(request.Content)) return "Markdown content is required.";
        if (string.IsNullOrWhiteSpace(request.Version) || request.Version.Trim().Length > 20) return "Version is required and limited to 20 characters.";
        if (string.IsNullOrWhiteSpace(request.LanguageCode) || request.LanguageCode.Trim().Length > 10) return "Language code is required and limited to 10 characters.";
        return request.Summary?.Length > 10000 ? "Summary is too long." : null;
    }
}
