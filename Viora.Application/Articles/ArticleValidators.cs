using FluentValidation;
using Viora.Domain.Entities;

namespace Viora.Application.Articles;

public sealed class CreateArticleValidator : AbstractValidator<CreateArticleCommand>
{
    public CreateArticleValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Visibility).IsInEnum();
        RuleFor(x => x.Blocks).Cascade(CascadeMode.Stop).NotNull().NotEmpty().WithErrorCode("ARTICLE_BLOCKS_REQUIRED");
        RuleFor(x => x.Blocks).Must(blocks => blocks is not null && HasRequiredBlocks(blocks))
            .WithMessage("Article phải có ít nhất một Heading và một Text.")
            .WithErrorCode("ARTICLE_REQUIRED_BLOCKS_MISSING");
        RuleFor(x => x.Blocks).Must(blocks => blocks is not null && HasContinuousOrder(blocks))
            .WithMessage("OrderIndex phải liên tục, bắt đầu từ 0 và không trùng.")
            .WithErrorCode("ARTICLE_BLOCK_ORDER_INVALID");
        RuleForEach(x => x.Blocks).SetValidator(new CreateArticleBlockValidator());
    }

    internal static bool HasRequiredBlocks(IReadOnlyList<CreateArticleBlockRequest> blocks) =>
        blocks.Any(x => x.Type == ArticleBlockType.Heading) &&
        blocks.Any(x => x.Type == ArticleBlockType.Text);

    internal static bool HasContinuousOrder<T>(IReadOnlyList<T> blocks) where T : notnull =>
        blocks.Select(GetOrder).OrderBy(x => x).SequenceEqual(Enumerable.Range(0, blocks.Count));

    private static int GetOrder<T>(T block) => block switch
    {
        CreateArticleBlockRequest value => value.OrderIndex,
        UpdateArticleBlockRequest value => value.OrderIndex,
        _ => -1
    };
}

public sealed class UpdateArticleValidator : AbstractValidator<UpdateArticleCommand>
{
    public UpdateArticleValidator()
    {
        RuleFor(x => x.ArticleId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Visibility).IsInEnum();
        RuleFor(x => x.Blocks).Cascade(CascadeMode.Stop).NotNull().NotEmpty().WithErrorCode("ARTICLE_BLOCKS_REQUIRED");
        RuleFor(x => x.Blocks).Must(blocks => blocks is not null &&
                blocks.Any(x => x.Type == ArticleBlockType.Heading) &&
                blocks.Any(x => x.Type == ArticleBlockType.Text))
            .WithErrorCode("ARTICLE_REQUIRED_BLOCKS_MISSING");
        RuleFor(x => x.Blocks).Must(blocks => blocks is not null && CreateArticleValidator.HasContinuousOrder(blocks))
            .WithErrorCode("ARTICLE_BLOCK_ORDER_INVALID");
        RuleFor(x => x.Blocks).Must(blocks => blocks is not null && blocks.Where(x => x.Id.HasValue).Select(x => x.Id).Distinct().Count() == blocks.Count(x => x.Id.HasValue))
            .WithErrorCode("ARTICLE_BLOCK_ID_DUPLICATE");
        RuleForEach(x => x.Blocks).SetValidator(new UpdateArticleBlockValidator());
    }
}

internal static class ArticleBlockRules
{
    public static bool IsValid(ArticleBlockType type, string? content, string? mediaUrl) => type switch
    {
        ArticleBlockType.Text or ArticleBlockType.Heading or ArticleBlockType.Quote or ArticleBlockType.Code => !string.IsNullOrWhiteSpace(content),
        ArticleBlockType.Embed => IsHttpsUrl(content),
        ArticleBlockType.Image or ArticleBlockType.Video => IsHttpsUrl(mediaUrl),
        ArticleBlockType.Divider => true,
        _ => false
    };

    public static bool IsOptionalHttpsUrl(string? value) => string.IsNullOrWhiteSpace(value) || IsHttpsUrl(value);
    private static bool IsHttpsUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

internal sealed class CreateArticleBlockValidator : AbstractValidator<CreateArticleBlockRequest>
{
    public CreateArticleBlockValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Content).MaximumLength(50000);
        RuleFor(x => x.Caption).MaximumLength(500);
        RuleFor(x => x).Must(x => ArticleBlockRules.IsValid(x.Type, x.Content, x.MediaUrl)).WithErrorCode("ARTICLE_BLOCK_EMPTY");
        RuleFor(x => x.ThumbnailUrl).Must(ArticleBlockRules.IsOptionalHttpsUrl).WithErrorCode("ARTICLE_BLOCK_URL_INVALID");
    }
}

internal sealed class UpdateArticleBlockValidator : AbstractValidator<UpdateArticleBlockRequest>
{
    public UpdateArticleBlockValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Content).MaximumLength(50000);
        RuleFor(x => x.Caption).MaximumLength(500);
        RuleFor(x => x).Must(x => ArticleBlockRules.IsValid(x.Type, x.Content, x.MediaUrl)).WithErrorCode("ARTICLE_BLOCK_EMPTY");
        RuleFor(x => x.ThumbnailUrl).Must(ArticleBlockRules.IsOptionalHttpsUrl).WithErrorCode("ARTICLE_BLOCK_URL_INVALID");
    }
}
