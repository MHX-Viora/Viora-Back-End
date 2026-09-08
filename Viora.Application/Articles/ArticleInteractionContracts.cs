using FluentValidation;
using MediatR;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Application.Articles;

public sealed record RecordArticleInteractionCommand(
    Guid UserId,
    Guid ArticleId,
    ArticleInteractionType InteractionType,
    int ReadDuration,
    decimal ReadPercentage) : IRequest<Result<ArticleInteractionResponse>>;

public sealed record ArticleInteractionResponse(
    Guid ArticleId,
    ArticleInteractionType InteractionType,
    int ReadDuration,
    decimal ReadPercentage);

public interface IArticleInteractionRepository
{
    Task<Result<ArticleInteractionResponse>> UpsertAsync(
        RecordArticleInteractionCommand command,
        CancellationToken cancellationToken);
}

public sealed class RecordArticleInteractionHandler(
    IArticleInteractionRepository repository,
    IValidator<RecordArticleInteractionCommand> validator)
    : IRequestHandler<RecordArticleInteractionCommand, Result<ArticleInteractionResponse>>
{
    public async Task<Result<ArticleInteractionResponse>> Handle(
        RecordArticleInteractionCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        return validation.IsValid
            ? await repository.UpsertAsync(request, cancellationToken)
            : Result<ArticleInteractionResponse>.Failure(
                PostInteractionError.Invalid,
                string.Join(" ", validation.Errors.Select(error => error.ErrorMessage)));
    }
}

public sealed class RecordArticleInteractionValidator
    : AbstractValidator<RecordArticleInteractionCommand>
{
    private static readonly ArticleInteractionType[] ClientTrackableTypes =
    [
        ArticleInteractionType.Impression,
        ArticleInteractionType.Open,
        ArticleInteractionType.View,
        ArticleInteractionType.NotInterested
    ];

    public RecordArticleInteractionValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ArticleId).NotEmpty();
        RuleFor(x => x.InteractionType)
            .Must(type => ClientTrackableTypes.Contains(type))
            .WithMessage("Loại tương tác không được phép ghi trực tiếp.");
        RuleFor(x => x.ReadDuration).InclusiveBetween(0, 86_400);
        RuleFor(x => x.ReadPercentage).InclusiveBetween(0, 100);
        RuleFor(x => x)
            .Must(x => x.InteractionType == ArticleInteractionType.View ||
                (x.ReadDuration == 0 && x.ReadPercentage == 0))
            .WithMessage("Chỉ tương tác View được chứa tiến độ đọc.");
    }
}
