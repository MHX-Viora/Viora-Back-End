using FluentValidation;
using Viora.Domain.Entities;

namespace Viora.Application.Posts;

public sealed class CreatePostValidator : AbstractValidator<CreatePostCommand>
{
    private static readonly string[] AllowedImageTypes = ["image/jpeg", "image/png", "image/webp"];

    public CreatePostValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Content).MaximumLength(5000);
        RuleFor(command => command.Visibility).Must(Enum.IsDefined);
        RuleFor(command => command.Latitude)
            .InclusiveBetween(-90, 90)
            .When(command => command.Latitude.HasValue);
        RuleFor(command => command.Longitude)
            .InclusiveBetween(-180, 180)
            .When(command => command.Longitude.HasValue);
        RuleFor(command => command.LocationName).MaximumLength(255);
        RuleFor(command => command.Link).MaximumLength(255);
        RuleFor(command => command.Files).Must(files => files.Count <= 10)
            .WithMessage("Tối đa 10 ảnh.");
        RuleFor(command => command).Must(command =>
                !string.IsNullOrWhiteSpace(command.Content) || command.Files.Count > 0)
            .WithMessage("Bài viết phải có nội dung hoặc ảnh.");
        RuleForEach(command => command.Files).ChildRules(file =>
        {
            file.RuleFor(x => x.ContentType)
                .Must(contentType => AllowedImageTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
                .WithMessage("Chỉ hỗ trợ ảnh JPEG, PNG hoặc WebP.");
            file.RuleFor(x => x.Length).GreaterThan(0)
                .WithMessage("Ảnh không được rỗng.");
        });
    }
}

public sealed class CreateReelValidator : AbstractValidator<CreateReelCommand>
{
    public CreateReelValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Content).MaximumLength(5000);
        RuleFor(command => command.Video).NotNull()
            .WithMessage("Bat buoc upload 1 file video.");
        RuleFor(command => command.Video!.Length)
            .GreaterThan(0)
            .When(command => command.Video is not null)
            .WithMessage("Video khong duoc rong.");
        RuleFor(command => command.Video!.ContentType)
            .Must(contentType => contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            .When(command => command.Video is not null)
            .WithMessage("Chi ho tro upload video.");
        RuleFor(command => command.Video!.ContentType)
            .Must(contentType => !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .When(command => command.Video is not null)
            .WithMessage("Khong cho upload anh.");
        RuleForEach(command => command.Hashtags)
            .MaximumLength(100);
    }
}
