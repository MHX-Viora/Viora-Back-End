using System.Security.Cryptography;
using System.Text;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Advertisements;
using Viora.Application.Articles;
using Viora.Application.Wallets;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Advertisements;

public sealed class AdvertisementService(
    AppDbContext dbContext,
    IWalletService walletService,
    IOptions<AdvertisementOptions> options) : IAdvertisementService
{
    private static readonly AdvertisementStatus[] OpenStatuses =
    [
        AdvertisementStatus.Draft,
        AdvertisementStatus.Pending,
        AdvertisementStatus.Approved,
        AdvertisementStatus.Active,
        AdvertisementStatus.Paused
    ];

    private readonly AdvertisementOptions settings = options.Value;

    public async Task<AdvertisementResponse> CreateAsync(Guid userId, CreateAdvertisementRequest request, CancellationToken cancellationToken)
    {
        AdvertisementRules.RequireBudget(request.TotalBudget);
        AdvertisementRules.RequireSchedule(request.StartAt, request.EndAt);
        AdvertisementRules.RequireDailyBudgetTotal(request.DailyBudget, request.TotalBudget, request.StartAt, request.EndAt);
        ValidateTargeting(request);
        var destinationUrl = AdvertisementRules.NormalizeDestinationUrl(request.DestinationUrl);
        AdvertisementRules.RequireCtaDestination(request.CtaType, destinationUrl);
        var now = DateTime.UtcNow;
        if (request.EndAt.ToUniversalTime() <= now)
            throw new AdvertisementValidationException("ADVERTISEMENT_END_IN_PAST", "Thời gian kết thúc quảng cáo phải ở tương lai.");

        var post = await dbContext.Posts
            .Include(item => item.User).ThenInclude(user => user.Account)
            .SingleOrDefaultAsync(item => item.Id == request.PostId, cancellationToken)
            ?? throw new AdvertisementNotFoundException();
        RequireAdvertisablePost(post, userId);

        var existing = await dbContext.Advertisements.SingleOrDefaultAsync(item =>
            item.PostId == post.Id && item.AdvertiserId == userId && OpenStatuses.Contains(item.Status), cancellationToken);
        if (existing is not null && existing.Status != AdvertisementStatus.Draft)
            throw new AdvertisementConflictException("ADVERTISEMENT_ALREADY_EXISTS", "Nội dung này đã có quảng cáo đang xử lý hoặc đang chạy.");

        if (existing is not null)
        {
            existing.Placement = PlacementFor(post.PostType);
            existing.Objective = request.Objective;
            existing.DestinationType = destinationUrl is null ? AdvertisementDestinationType.InAppContent : AdvertisementDestinationType.ExternalUrl;
            existing.DestinationUrl = destinationUrl;
            existing.CtaType = request.CtaType;
            existing.TargetingMode = request.TargetingMode;
            existing.MinimumAge = request.TargetingMode == AdvertisementTargetingMode.Custom ? request.MinimumAge : null;
            existing.MaximumAge = request.TargetingMode == AdvertisementTargetingMode.Custom ? request.MaximumAge : null;
            existing.TargetLocation = request.TargetingMode == AdvertisementTargetingMode.Custom ? Normalize(request.TargetLocation) : null;
            existing.DailyBudget = request.DailyBudget;
            existing.TotalBudget = request.TotalBudget;
            existing.StartAt = request.StartAt.ToUniversalTime();
            existing.EndAt = request.EndAt.ToUniversalTime();
            await dbContext.SaveChangesAsync(cancellationToken);
            return await LoadResponseAsync(existing.Id, cancellationToken);
        }

        var advertisement = new Advertisement
        {
            PostId = post.Id,
            AdvertiserId = userId,
            Placement = PlacementFor(post.PostType),
            Objective = request.Objective,
            DestinationType = destinationUrl is null ? AdvertisementDestinationType.InAppContent : AdvertisementDestinationType.ExternalUrl,
            DestinationUrl = destinationUrl,
            CtaType = request.CtaType,
            TargetingMode = request.TargetingMode,
            MinimumAge = request.TargetingMode == AdvertisementTargetingMode.Custom ? request.MinimumAge : null,
            MaximumAge = request.TargetingMode == AdvertisementTargetingMode.Custom ? request.MaximumAge : null,
            TargetLocation = request.TargetingMode == AdvertisementTargetingMode.Custom ? Normalize(request.TargetLocation) : null,
            DailyBudget = request.DailyBudget,
            TotalBudget = request.TotalBudget,
            StartAt = request.StartAt.ToUniversalTime(),
            EndAt = request.EndAt.ToUniversalTime(),
            Status = AdvertisementStatus.Draft
        };
        dbContext.Advertisements.Add(advertisement);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadResponseAsync(advertisement.Id, cancellationToken);
    }

    public async Task<AdvertisementResponse> SubmitAsync(Guid userId, Guid advertisementId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var advertisement = await LoadOwnedEntityAsync(userId, advertisementId, cancellationToken, includePost: true);
        AdvertisementRules.RequireTransition(advertisement.Status, AdvertisementStatus.Pending);
        RequireAdvertisablePost(advertisement.Post, userId);
        AdvertisementRules.RequireBudget(advertisement.TotalBudget);
        AdvertisementRules.RequireSchedule(advertisement.StartAt, advertisement.EndAt);
        AdvertisementRules.RequireDailyBudgetTotal(advertisement.DailyBudget, advertisement.TotalBudget, advertisement.StartAt, advertisement.EndAt);
        if (advertisement.EndAt <= DateTime.UtcNow)
            throw new AdvertisementValidationException("ADVERTISEMENT_END_IN_PAST", "Thời gian kết thúc quảng cáo phải ở tương lai.");

        try
        {
            await walletService.HoldAsync(
                userId, advertisement.TotalBudget, "Advertisement", advertisement.Id.ToString("D"),
                $"advertisement:{advertisement.Id:D}:reserve", cancellationToken);
        }
        catch (InsufficientWalletBalanceException exception)
        {
            throw new AdvertisementInsufficientBalanceException(exception.Available, exception.Requested);
        }

        advertisement.ReservedAmount = advertisement.TotalBudget;
        advertisement.Status = AdvertisementStatus.Pending;
        advertisement.SubmittedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        return await LoadResponseAsync(advertisement.Id, cancellationToken);
    }

    public Task<AdvertisementResponse> PauseAsync(Guid userId, Guid advertisementId, CancellationToken cancellationToken) =>
        ChangeOwnerStatusAsync(userId, advertisementId, AdvertisementStatus.Paused, cancellationToken);

    public async Task<AdvertisementResponse> ResumeAsync(Guid userId, Guid advertisementId, CancellationToken cancellationToken)
    {
        var advertisement = await LoadOwnedEntityAsync(userId, advertisementId, cancellationToken, includePost: true);
        AdvertisementRules.RequireTransition(advertisement.Status, AdvertisementStatus.Active);
        if (!IsAdvertisablePost(advertisement.Post))
            throw new AdvertisementConflictException("ADVERTISEMENT_CONTENT_UNAVAILABLE", "Nội dung quảng cáo không còn công khai hoặc đủ điều kiện.");
        if (advertisement.EndAt <= DateTime.UtcNow || advertisement.ReservedAmount <= 0)
            throw new AdvertisementConflictException("ADVERTISEMENT_CANNOT_RESUME", "Quảng cáo đã hết hạn hoặc hết ngân sách.");
        if (advertisement.StartAt > DateTime.UtcNow)
            throw new AdvertisementConflictException("ADVERTISEMENT_NOT_STARTED", "Quảng cáo chưa đến thời gian bắt đầu.");
        advertisement.Status = AdvertisementStatus.Active;
        advertisement.ActivatedAt ??= DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadResponseAsync(advertisement.Id, cancellationToken);
    }

    public async Task<AdvertisementResponse> CancelAsync(Guid userId, Guid advertisementId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var advertisement = await LoadOwnedEntityAsync(userId, advertisementId, cancellationToken);
        if (advertisement.Status == AdvertisementStatus.Cancelled)
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            return await LoadResponseAsync(advertisement.Id, cancellationToken);
        }
        AdvertisementRules.RequireTransition(advertisement.Status, AdvertisementStatus.Cancelled);
        await ReleaseRemainingAsync(advertisement, "cancel", cancellationToken);
        advertisement.Status = AdvertisementStatus.Cancelled;
        advertisement.CancelledAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        return await LoadResponseAsync(advertisement.Id, cancellationToken);
    }

    public async Task<AdvertisementResponse> GetAsync(Guid userId, Guid advertisementId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Advertisements.AnyAsync(item => item.Id == advertisementId, cancellationToken))
            throw new AdvertisementNotFoundException();
        if (!await dbContext.Advertisements.AnyAsync(item => item.Id == advertisementId && item.AdvertiserId == userId, cancellationToken))
            throw new AdvertisementForbiddenException("Bạn không có quyền xem quảng cáo này.");
        return await LoadResponseAsync(advertisementId, cancellationToken);
    }

    public Task<AdvertisementPage> GetMineAsync(Guid userId, int page, int pageSize, AdvertisementStatus? status, CancellationToken cancellationToken) =>
        GetPageAsync(dbContext.Advertisements.Where(item => item.AdvertiserId == userId), page, pageSize, status, cancellationToken);

    public async Task<AdvertisementDeliveryResponse> GetDeliveryAsync(Guid viewerId, AdvertisementPlacement placement, int take, CancellationToken cancellationToken)
    {
        await SynchronizeLifecycleAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var todayUtc = now.Date;
        var boundedTake = Math.Clamp(take, 1, Math.Max(1, settings.MaximumDeliveryItems));
        var birthday = await dbContext.UserIdentities.AsNoTracking()
            .Where(item => item.UserId == viewerId && item.Status == IdentitySubmissionStatus.Approved && item.Birthday.HasValue)
            .OrderByDescending(item => item.ReviewedAt)
            .Select(item => item.Birthday)
            .FirstOrDefaultAsync(cancellationToken);
        int? viewerAge = birthday.HasValue
            ? now.Year - birthday.Value.Year - (DateOnly.FromDateTime(now) < birthday.Value.AddYears(now.Year - birthday.Value.Year) ? 1 : 0)
            : null;
        var hiddenIds = dbContext.AdvertisementFeedback
            .Where(item => item.ViewerId == viewerId &&
                           (item.Type == AdvertisementFeedbackType.Hide || item.Type == AdvertisementFeedbackType.NotInterested))
            .Select(item => item.AdvertisementId);
        var items = await BaseQuery()
            .Where(item => item.Status == AdvertisementStatus.Active &&
                           item.Placement == placement &&
                           item.AdvertiserId != viewerId &&
                           item.Post.Status == PostStatus.Published &&
                           item.Post.Visibility == PostVisibility.Public &&
                           item.Post.DeletedAt == null &&
                           item.Post.User.Account.Status == AccountStatus.Active &&
                           item.Post.User.AccountStyle >= AccountStyle.Creator &&
                           item.Post.User.AccountStyle <= AccountStyle.Agency &&
                           item.StartAt <= now && item.EndAt > now &&
                           item.ReservedAmount > 0 &&
                           (!item.DailyBudget.HasValue ||
                            (dbContext.AdvertisementEvents.Where(adEvent => adEvent.AdvertisementId == item.Id && adEvent.CreatedAt >= todayUtc)
                                .Sum(adEvent => (decimal?)adEvent.ChargeAmount) ?? 0m) < item.DailyBudget.Value) &&
                           (item.TargetingMode == AdvertisementTargetingMode.Automatic ||
                            (viewerAge.HasValue &&
                             (!item.MinimumAge.HasValue || viewerAge.Value >= item.MinimumAge.Value) &&
                             (!item.MaximumAge.HasValue || viewerAge.Value <= item.MaximumAge.Value))) &&
                           !hiddenIds.Contains(item.Id))
            .OrderBy(item => item.SpentAmount / item.TotalBudget)
            .ThenBy(item => item.CreatedAt)
            .Take(boundedTake)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return new AdvertisementDeliveryResponse(await MapManyAsync(items, cancellationToken));
    }

    public async Task<AdvertisementEventResponse> RecordEventAsync(
        Guid viewerId, Guid advertisementId, AdvertisementEventType type,
        AdvertisementEventRequest request, CancellationToken cancellationToken)
    {
        var eventKey = NormalizeEventKey(request.ClientEventId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var advertisement = await dbContext.Advertisements
            .FromSqlInterpolated($"SELECT * FROM \"Advertisements\" WHERE \"Id\" = {advertisementId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new AdvertisementNotFoundException();
        var existing = await dbContext.AdvertisementEvents.AsNoTracking().SingleOrDefaultAsync(item =>
            item.AdvertisementId == advertisementId && item.ViewerId == viewerId &&
            item.Type == type && item.ClientEventId == eventKey, cancellationToken);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EventResponse(existing, advertisement, true);
        }

        var now = DateTime.UtcNow;
        if (type == AdvertisementEventType.Impression)
        {
            var lastImpression = await dbContext.AdvertisementEvents.AsNoTracking()
                .Where(item => item.AdvertisementId == advertisementId && item.ViewerId == viewerId && item.Type == AdvertisementEventType.Impression)
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (lastImpression is not null && AdvertisementRules.IsRepeatedImpression(lastImpression.CreatedAt, now))
            {
                await transaction.RollbackAsync(cancellationToken);
                return EventResponse(lastImpression, advertisement, true);
            }
        }
        if (advertisement.AdvertiserId == viewerId)
            throw new AdvertisementForbiddenException("Không ghi nhận tương tác trên quảng cáo của chính bạn.");
        if (advertisement.Status != AdvertisementStatus.Active || advertisement.StartAt > now || advertisement.EndAt <= now || advertisement.ReservedAmount <= 0)
            throw new AdvertisementConflictException("ADVERTISEMENT_NOT_ACTIVE", "Quảng cáo hiện không được phân phối.");
        var post = await dbContext.Posts.AsNoTracking()
            .Include(item => item.User).ThenInclude(user => user.Account)
            .SingleOrDefaultAsync(item => item.Id == advertisement.PostId, cancellationToken);
        if (post is null || !IsAdvertisablePost(post))
            throw new AdvertisementConflictException("ADVERTISEMENT_CONTENT_UNAVAILABLE", "Nội dung quảng cáo không còn công khai hoặc đủ điều kiện.");

        var unitPrice = type == AdvertisementEventType.Impression ? settings.ImpressionPrice : settings.ClickPrice;
        decimal? dailyRemaining = null;
        if (advertisement.DailyBudget.HasValue)
        {
            var dailySpent = await dbContext.AdvertisementEvents.AsNoTracking()
                .Where(item => item.AdvertisementId == advertisementId && item.CreatedAt >= now.Date)
                .SumAsync(item => (decimal?)item.ChargeAmount, cancellationToken) ?? 0m;
            dailyRemaining = advertisement.DailyBudget.Value - dailySpent;
        }
        var charge = AdvertisementRules.CalculateCharge(unitPrice, advertisement.SpentAmount, advertisement.TotalBudget, dailyRemaining);
        if (charge <= 0)
            throw new AdvertisementConflictException(dailyRemaining <= 0 ? "ADVERTISEMENT_DAILY_BUDGET_EXHAUSTED" : "ADVERTISEMENT_BUDGET_EXHAUSTED", "Quảng cáo đã hết ngân sách hôm nay hoặc tổng ngân sách.");
        var ledgerKey = BuildEventLedgerKey(advertisementId, viewerId, type, eventKey);
        await walletService.CaptureAsync(advertisement.AdvertiserId, charge, "AdvertisementEvent", advertisementId.ToString("D"), ledgerKey, cancellationToken);

        var trackedEvent = new AdvertisementEvent
        {
            AdvertisementId = advertisementId,
            ViewerId = viewerId,
            Type = type,
            ClientEventId = eventKey,
            ChargeAmount = charge
        };
        advertisement.SpentAmount += charge;
        advertisement.ReservedAmount -= charge;
        if (advertisement.ReservedAmount <= 0)
        {
            advertisement.ReservedAmount = 0;
            advertisement.Status = AdvertisementStatus.Completed;
            advertisement.CompletedAt = now;
        }
        dbContext.AdvertisementEvents.Add(trackedEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return EventResponse(trackedEvent, advertisement, false);
    }

    public async Task<AdvertisementFeedbackResponse> RecordFeedbackAsync(
        Guid viewerId, Guid advertisementId, AdvertisementFeedbackRequest request, CancellationToken cancellationToken)
    {
        if (!await dbContext.Advertisements.AnyAsync(item => item.Id == advertisementId, cancellationToken))
            throw new AdvertisementNotFoundException();
        var reason = Normalize(request.Reason);
        if (reason?.Length > 500)
            throw new AdvertisementValidationException("ADVERTISEMENT_FEEDBACK_TOO_LONG", "Nội dung phản hồi không được vượt quá 500 ký tự.");
        if (request.Type == AdvertisementFeedbackType.Report && reason is null)
            throw new AdvertisementValidationException("ADVERTISEMENT_REPORT_REASON_REQUIRED", "Cần nhập lý do báo cáo quảng cáo.");
        var existing = await dbContext.AdvertisementFeedback.SingleOrDefaultAsync(item =>
            item.AdvertisementId == advertisementId && item.ViewerId == viewerId && item.Type == request.Type, cancellationToken);
        if (existing is not null) return new(existing.Id, existing.Type);
        var feedback = new AdvertisementFeedback
        {
            AdvertisementId = advertisementId,
            ViewerId = viewerId,
            Type = request.Type,
            Reason = reason
        };
        dbContext.AdvertisementFeedback.Add(feedback);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(feedback.Id, feedback.Type);
    }

    public Task<AdvertisementPage> GetAdminPageAsync(int page, int pageSize, AdvertisementStatus? status, CancellationToken cancellationToken) =>
        GetPageAsync(dbContext.Advertisements, page, pageSize, status, cancellationToken);

    public async Task<AdvertisementResponse> ApproveAsync(Guid adminId, Guid advertisementId, CancellationToken cancellationToken)
    {
        var advertisement = await dbContext.Advertisements
            .Include(item => item.Post).ThenInclude(post => post.User).ThenInclude(user => user.Account)
            .SingleOrDefaultAsync(item => item.Id == advertisementId, cancellationToken)
            ?? throw new AdvertisementNotFoundException("POST_NOT_FOUND", "Không tìm thấy bài viết.");
        AdvertisementRules.RequireTransition(advertisement.Status, AdvertisementStatus.Approved);
        var now = DateTime.UtcNow;
        if (advertisement.EndAt <= now || advertisement.ReservedAmount <= 0)
            throw new AdvertisementConflictException("ADVERTISEMENT_EXPIRED", "Quảng cáo đã hết hạn hoặc hết ngân sách.");
        if (!IsAdvertisablePost(advertisement.Post))
            throw new AdvertisementConflictException("ADVERTISEMENT_CONTENT_UNAVAILABLE", "Nội dung quảng cáo không còn công khai hoặc đủ điều kiện.");
        advertisement.Status = advertisement.StartAt <= now ? AdvertisementStatus.Active : AdvertisementStatus.Approved;
        advertisement.ReviewedBy = adminId;
        advertisement.ReviewedAt = now;
        advertisement.ApprovedAt = now;
        advertisement.ActivatedAt = advertisement.Status == AdvertisementStatus.Active ? now : null;
        advertisement.ReviewReason = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadResponseAsync(advertisement.Id, cancellationToken);
    }

    public async Task<AdvertisementResponse> RejectAsync(Guid adminId, Guid advertisementId, string reason, CancellationToken cancellationToken)
    {
        AdvertisementRules.RequireReviewReason(AdvertisementStatus.Rejected, reason);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var advertisement = await dbContext.Advertisements.SingleOrDefaultAsync(item => item.Id == advertisementId, cancellationToken)
            ?? throw new AdvertisementNotFoundException();
        AdvertisementRules.RequireTransition(advertisement.Status, AdvertisementStatus.Rejected);
        await ReleaseRemainingAsync(advertisement, "reject", cancellationToken);
        advertisement.Status = AdvertisementStatus.Rejected;
        advertisement.ReviewReason = reason.Trim();
        advertisement.ReviewedBy = adminId;
        advertisement.ReviewedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        return await LoadResponseAsync(advertisement.Id, cancellationToken);
    }

    public async Task SynchronizeLifecycleAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = DateTime.UtcNow;
        var starting = await dbContext.Advertisements
            .Where(item => item.Status == AdvertisementStatus.Approved && item.StartAt <= now && item.EndAt > now && item.ReservedAmount > 0)
            .ToListAsync(cancellationToken);
        foreach (var item in starting)
        {
            item.Status = AdvertisementStatus.Active;
            item.ActivatedAt ??= now;
        }

        var ending = await dbContext.Advertisements
            .Where(item => (item.Status == AdvertisementStatus.Approved || item.Status == AdvertisementStatus.Active || item.Status == AdvertisementStatus.Paused) &&
                           (item.EndAt <= now || item.ReservedAmount <= 0))
            .ToListAsync(cancellationToken);
        foreach (var item in ending)
        {
            await ReleaseRemainingAsync(item, "complete", cancellationToken);
            item.Status = AdvertisementStatus.Completed;
            item.CompletedAt ??= now;
        }
        if (starting.Count > 0 || ending.Count > 0) await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<AdvertisementResponse> ChangeOwnerStatusAsync(Guid userId, Guid id, AdvertisementStatus next, CancellationToken cancellationToken)
    {
        var advertisement = await LoadOwnedEntityAsync(userId, id, cancellationToken);
        AdvertisementRules.RequireTransition(advertisement.Status, next);
        advertisement.Status = next;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadResponseAsync(advertisement.Id, cancellationToken);
    }

    private async Task<Advertisement> LoadOwnedEntityAsync(Guid userId, Guid id, CancellationToken cancellationToken, bool includePost = false)
    {
        IQueryable<Advertisement> query = dbContext.Advertisements;
        if (includePost) query = query.Include(item => item.Post).ThenInclude(post => post.User).ThenInclude(user => user.Account);
        var advertisement = await query.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new AdvertisementNotFoundException();
        if (advertisement.AdvertiserId != userId)
            throw new AdvertisementForbiddenException("Bạn không có quyền thay đổi quảng cáo này.");
        return advertisement;
    }

    private async Task ReleaseRemainingAsync(Advertisement advertisement, string operation, CancellationToken cancellationToken)
    {
        if (advertisement.ReservedAmount <= 0) return;
        await walletService.ReleaseAsync(
            advertisement.AdvertiserId, advertisement.ReservedAmount, "Advertisement", advertisement.Id.ToString("D"),
            $"advertisement:{advertisement.Id:D}:{operation}:release", cancellationToken);
        advertisement.ReservedAmount = 0;
    }

    private async Task<AdvertisementPage> GetPageAsync(
        IQueryable<Advertisement> source, int page, int pageSize, AdvertisementStatus? status, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        if (status.HasValue) source = source.Where(item => item.Status == status.Value);
        var total = await source.CountAsync(cancellationToken);
        var items = await WithContent(source).OrderByDescending(item => item.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).AsSplitQuery().ToListAsync(cancellationToken);
        return new(await MapManyAsync(items, cancellationToken), page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize));
    }

    private async Task<AdvertisementResponse> LoadResponseAsync(Guid id, CancellationToken cancellationToken)
    {
        var advertisement = await BaseQuery().AsSplitQuery().SingleAsync(item => item.Id == id, cancellationToken);
        var counts = await EventCountsAsync([id], cancellationToken);
        return Map(advertisement, counts.GetValueOrDefault(id));
    }

    private async Task<IReadOnlyList<AdvertisementResponse>> MapManyAsync(IReadOnlyList<Advertisement> items, CancellationToken cancellationToken)
    {
        var counts = await EventCountsAsync(items.Select(item => item.Id).ToArray(), cancellationToken);
        return items.Select(item => Map(item, counts.GetValueOrDefault(item.Id))).ToList();
    }

    private async Task<Dictionary<Guid, EventCounts>> EventCountsAsync(Guid[] ids, CancellationToken cancellationToken) =>
        await dbContext.AdvertisementEvents.AsNoTracking().Where(item => ids.Contains(item.AdvertisementId))
            .GroupBy(item => item.AdvertisementId)
            .Select(group => new { Id = group.Key, Impressions = group.LongCount(item => item.Type == AdvertisementEventType.Impression), Clicks = group.LongCount(item => item.Type == AdvertisementEventType.Click) })
            .ToDictionaryAsync(item => item.Id, item => new EventCounts(item.Impressions, item.Clicks), cancellationToken);

    private IQueryable<Advertisement> BaseQuery() => WithContent(dbContext.Advertisements.AsNoTracking());

    private static IQueryable<Advertisement> WithContent(IQueryable<Advertisement> query) => query
        .Include(item => item.Post).ThenInclude(post => post.User)
        .Include(item => item.Post).ThenInclude(post => post.Media)
        .Include(item => item.Post).ThenInclude(post => post.ArticleBlocks);

    private static AdvertisementResponse Map(Advertisement item, EventCounts counts)
    {
        var post = item.Post;
        var orderedBlocks = post.ArticleBlocks.OrderBy(block => block.OrderIndex).ToList();
        var article = post.PostType == PostType.Article
            ? new AdvertisementArticleResponse(
                post.Content ?? "Bài viết",
                orderedBlocks.FirstOrDefault(block => block.BlockType == ArticleBlockType.Image)?.MediaUrl,
                Normalize(orderedBlocks.FirstOrDefault(block => block.BlockType == ArticleBlockType.Text)?.Content),
                ArticleReadingTime.Calculate(orderedBlocks.Select(block => block.Content)))
            : null;
        var ctr = counts.Impressions == 0 ? 0m : decimal.Round(counts.Clicks * 100m / counts.Impressions, 2);
        return new(
            item.Id, item.PostId, item.AdvertiserId, item.Placement, item.Objective, item.DestinationType,
            item.DestinationUrl, item.CtaType, item.TargetingMode, item.MinimumAge, item.MaximumAge,
            item.TargetLocation, item.DailyBudget, item.TotalBudget, item.SpentAmount, item.ReservedAmount,
            item.StartAt, item.EndAt, item.Status, item.ReviewReason, item.CreatedAt, item.UpdatedAt,
            counts.Impressions, counts.Clicks, ctr,
            new(post.Id, post.PostType, post.Content, post.Location, post.Link, post.CreatedAt,
                post.ReactionCount, post.CommentCount, post.ShareCount, post.SaveCount, post.ViewCount,
                new(post.User.Id, post.User.DisplayName, post.User.AvatarUrl, post.User.IsVerified, post.User.AccountStyle),
                post.Media.Select(media => new AdvertisementMediaResponse(media.Id, media.MediaUrl, media.ThumbnailUrl)).ToList(), article));
    }

    private static AdvertisementEventResponse EventResponse(AdvertisementEvent trackedEvent, Advertisement advertisement, bool duplicate) =>
        new(trackedEvent.Id, duplicate, trackedEvent.ChargeAmount, advertisement.SpentAmount, advertisement.ReservedAmount, advertisement.Status);

    private static void RequireAdvertisablePost(Post post, Guid userId)
    {
        if (post.UserId != userId) throw new AdvertisementForbiddenException("Bạn chỉ có thể quảng cáo nội dung của chính mình.");
        if (post.Status != PostStatus.Published || post.Visibility != PostVisibility.Public || post.DeletedAt.HasValue)
            throw new AdvertisementValidationException("POST_NOT_ADVERTISABLE", "Chỉ nội dung công khai đã xuất bản mới có thể chạy quảng cáo.");
        if (post.User.Account.Status != AccountStatus.Active || !post.User.AccountStyle.CanAdvertise())
            throw new AdvertisementForbiddenException("Loại tài khoản này chưa đủ điều kiện chạy quảng cáo.");
    }

    private static bool IsAdvertisablePost(Post post) =>
        post.Status == PostStatus.Published && post.Visibility == PostVisibility.Public &&
        !post.DeletedAt.HasValue && post.User.Account.Status == AccountStatus.Active &&
        post.User.AccountStyle.CanAdvertise();

    private static AdvertisementPlacement PlacementFor(PostType postType) => postType switch
    {
        PostType.ShortVideo => AdvertisementPlacement.Reels,
        PostType.Article => AdvertisementPlacement.News,
        _ => AdvertisementPlacement.Feed
    };

    private static void ValidateTargeting(CreateAdvertisementRequest request)
    {
        if (request.DailyBudget.HasValue)
        {
            AdvertisementRules.RequireBudget(request.DailyBudget.Value);
            if (request.DailyBudget > request.TotalBudget)
                throw new AdvertisementValidationException("INVALID_DAILY_BUDGET", "Ngân sách ngày không được lớn hơn tổng ngân sách.");
        }
        if (request.TargetingMode != AdvertisementTargetingMode.Custom) return;
        if (request.MinimumAge is < 13 or > 100 || request.MaximumAge is < 13 or > 100 ||
            request.MinimumAge.HasValue && request.MaximumAge.HasValue && request.MinimumAge > request.MaximumAge)
            throw new AdvertisementValidationException("INVALID_TARGET_AGE", "Độ tuổi mục tiêu phải từ 13 đến 100 và khoảng tuổi phải hợp lệ.");
        if (request.TargetLocation?.Trim().Length > 120)
            throw new AdvertisementValidationException("INVALID_TARGET_LOCATION", "Khu vực mục tiêu không được vượt quá 120 ký tự.");
        if (!string.IsNullOrWhiteSpace(request.TargetLocation))
            throw new AdvertisementValidationException("TARGET_LOCATION_UNAVAILABLE", "Chưa hỗ trợ nhắm mục tiêu theo khu vực.");
    }

    private static string NormalizeEventKey(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 100)
            throw new AdvertisementValidationException("INVALID_ADVERTISEMENT_EVENT_ID", "Mã sự kiện quảng cáo không hợp lệ.");
        return normalized;
    }

    private static string BuildEventLedgerKey(Guid adId, Guid viewerId, AdvertisementEventType type, string eventKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{viewerId:D}:{type}:{eventKey}"))).ToLowerInvariant();
        return $"advertisement:{adId:D}:event:{hash}";
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private readonly record struct EventCounts(long Impressions, long Clicks);
}
