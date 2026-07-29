using Microsoft.EntityFrameworkCore;
using Viora.Application.Legal;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class LegalDocumentRepository(AppDbContext db) : ILegalDocumentRepository
{
    public async Task<IReadOnlyList<LegalDocumentSummaryResponse>> GetPublishedAsync(CancellationToken token) =>
        await db.LegalDocuments.AsNoTracking().Where(x => x.IsPublished)
            .OrderBy(x => x.Type).ThenBy(x => x.LanguageCode)
            .Select(x => new LegalDocumentSummaryResponse(x.Id, x.Type, x.Title, x.Summary, x.LanguageCode, x.Version, x.IsPublished, x.PublishedAt, x.CreatedAt, x.UpdatedAt)).ToListAsync(token);

    public Task<LegalDocumentResponse?> GetPublishedAsync(LegalDocumentType type, CancellationToken token) =>
        db.LegalDocuments.AsNoTracking().Where(x => x.Type == type && x.LanguageCode == "vi" && x.IsPublished)
            .Select(x => new LegalDocumentResponse(x.Id, x.Type, x.Title, x.Summary, x.Content, x.LanguageCode, x.Version, x.IsPublished, x.PublishedAt, x.CreatedBy, x.UpdatedBy, x.CreatedAt, x.UpdatedAt)).SingleOrDefaultAsync(token);

    public async Task<IReadOnlyList<LegalDocumentSummaryResponse>> GetAllAsync(CancellationToken token) =>
        await db.LegalDocuments.AsNoTracking().OrderBy(x => x.Type).ThenByDescending(x => x.CreatedAt)
            .Select(x => new LegalDocumentSummaryResponse(x.Id, x.Type, x.Title, x.Summary, x.LanguageCode, x.Version, x.IsPublished, x.PublishedAt, x.CreatedAt, x.UpdatedAt)).ToListAsync(token);

    public Task<LegalDocumentResponse?> GetByIdAsync(Guid id, CancellationToken token) =>
        db.LegalDocuments.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new LegalDocumentResponse(x.Id, x.Type, x.Title, x.Summary, x.Content, x.LanguageCode, x.Version, x.IsPublished, x.PublishedAt, x.CreatedBy, x.UpdatedBy, x.CreatedAt, x.UpdatedAt)).SingleOrDefaultAsync(token);

    public async Task<LegalMutationResult> CreateAsync(Guid adminId, SaveLegalDocumentRequest request, CancellationToken token)
    {
        var validation = LegalDocumentRules.Validate(request);
        if (validation is not null) return LegalMutationResult.Fail(LegalMutationError.Invalid, validation);
        if (await db.LegalDocuments.AnyAsync(x => x.Type == request.Type && x.LanguageCode == request.LanguageCode.Trim() && x.Version == request.Version.Trim(), token))
            return LegalMutationResult.Fail(LegalMutationError.Conflict, "This type, language, and version already exists.");
        var entity = NewEntity(adminId, request);
        db.LegalDocuments.Add(entity);
        await db.SaveChangesAsync(token);
        return LegalMutationResult.Ok(Detail(entity));
    }

    public async Task<LegalMutationResult> CreateVersionAsync(Guid adminId, Guid sourceId, SaveLegalDocumentRequest request, CancellationToken token)
    {
        var source = await db.LegalDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId, token);
        if (source is null) return LegalMutationResult.Fail(LegalMutationError.NotFound, "Document not found.");
        if (source.Type != request.Type || source.LanguageCode != request.LanguageCode.Trim())
            return LegalMutationResult.Fail(LegalMutationError.Invalid, "Type and language cannot change between versions.");
        return await CreateAsync(adminId, request, token);
    }

    public async Task<LegalMutationResult> PublishAsync(Guid adminId, Guid id, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var document = await db.LegalDocuments.SingleOrDefaultAsync(x => x.Id == id, token);
        if (document is null) return LegalMutationResult.Fail(LegalMutationError.NotFound, "Document not found.");
        var published = await db.LegalDocuments.Where(x => x.Type == document.Type && x.LanguageCode == document.LanguageCode && x.IsPublished && x.Id != id).ToListAsync(token);
        foreach (var old in published) { old.IsPublished = false; old.UpdatedBy = adminId; }
        document.IsPublished = true;
        document.PublishedAt = DateTime.UtcNow;
        document.UpdatedBy = adminId;
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return LegalMutationResult.Ok(Detail(document));
    }

    public async Task<LegalMutationResult> DeleteDraftAsync(Guid id, CancellationToken token)
    {
        var document = await db.LegalDocuments.SingleOrDefaultAsync(x => x.Id == id, token);
        if (document is null) return LegalMutationResult.Fail(LegalMutationError.NotFound, "Document not found.");
        if (document.IsPublished || await db.UserLegalAcceptances.AnyAsync(x => x.LegalDocumentId == id, token))
            return LegalMutationResult.Fail(LegalMutationError.Conflict, "Published or accepted document history cannot be deleted.");
        db.LegalDocuments.Remove(document);
        await db.SaveChangesAsync(token);
        return LegalMutationResult.Ok();
    }

    public async Task<LegalMutationResult> AcceptAsync(Guid userId, Guid documentId, string version, string? appVersion, short? deviceType, string? ipAddress, CancellationToken token)
    {
        var document = await db.LegalDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId, token);
        if (document is null || !document.IsPublished) return LegalMutationResult.Fail(LegalMutationError.NotFound, "Published document not found.");
        if (!string.Equals(document.Version, version?.Trim(), StringComparison.Ordinal))
            return LegalMutationResult.Fail(LegalMutationError.Conflict, "Document version does not match.");
        if (!await db.UserLegalAcceptances.AnyAsync(x => x.UserId == userId && x.LegalDocumentId == documentId && x.Version == document.Version, token))
        {
            db.UserLegalAcceptances.Add(new UserLegalAcceptance { Id = Guid.NewGuid(), UserId = userId, LegalDocumentId = documentId, Version = document.Version, AcceptedAt = DateTime.UtcNow, AppVersion = appVersion, DeviceType = deviceType, IpAddress = ipAddress });
            await db.SaveChangesAsync(token);
        }
        return LegalMutationResult.Ok();
    }

    private static LegalDocument NewEntity(Guid adminId, SaveLegalDocumentRequest r) => new()
    {
        Id = Guid.NewGuid(), Type = r.Type, Title = r.Title.Trim(), Summary = r.Summary?.Trim(),
        Content = r.Content, LanguageCode = r.LanguageCode.Trim().ToLowerInvariant(), Version = r.Version.Trim(),
        CreatedBy = adminId, UpdatedBy = adminId
    };
    private static LegalDocumentSummaryResponse Summary(LegalDocument x) => new(x.Id, x.Type, x.Title, x.Summary, x.LanguageCode, x.Version, x.IsPublished, x.PublishedAt, x.CreatedAt, x.UpdatedAt);
    private static LegalDocumentResponse Detail(LegalDocument x) => new(x.Id, x.Type, x.Title, x.Summary, x.Content, x.LanguageCode, x.Version, x.IsPublished, x.PublishedAt, x.CreatedBy, x.UpdatedBy, x.CreatedAt, x.UpdatedAt);
}
