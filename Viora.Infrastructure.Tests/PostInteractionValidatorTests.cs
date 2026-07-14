using Viora.Application.Posts;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class PostInteractionValidatorTests
{
    [Fact]
    public async Task Reaction_validator_rejects_unknown_reaction_type()
    {
        var validator = new ReactPostValidator();

        var result = await validator.ValidateAsync(new ReactPostCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            (ReactionType)99));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Comment_validator_rejects_empty_content()
    {
        var validator = new CreateCommentValidator();

        var result = await validator.ValidateAsync(new CreateCommentCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ""));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Report_validator_accepts_supported_reason()
    {
        var validator = new ReportPostValidator();

        var result = await validator.ValidateAsync(new ReportPostCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ReportReason.Scam,
            "Nội dung lừa đảo"));

        Assert.True(result.IsValid);
    }
}
