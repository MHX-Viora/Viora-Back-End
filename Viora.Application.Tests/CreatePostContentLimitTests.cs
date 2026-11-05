using System.ComponentModel.DataAnnotations;
using Viora.Application.Posts;
using Viora.Domain.Entities;
using viora_BE.Controllers;
using Xunit;

public sealed class CreatePostContentLimitTests
{
    [Theory]
    [InlineData(49999, true)]
    [InlineData(50000, true)]
    [InlineData(50001, false)]
    public void Community_post_content_has_matching_command_and_form_limits(int length, bool expected)
    {
        var content = new string('a', length);
        var command = new CreatePostCommand(Guid.NewGuid(), content, PostVisibility.Public,
            null, null, null, null, []);
        Assert.Equal(expected, new CreatePostValidator().Validate(command).IsValid);

        var form = new CreatePostFormRequest { Content = content };
        Assert.Equal(expected, Validator.TryValidateObject(form, new ValidationContext(form), [], true));
    }
}
