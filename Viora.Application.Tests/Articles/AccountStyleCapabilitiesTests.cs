using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Articles;

public sealed class AccountStyleCapabilitiesTests
{
    [Fact]
    public void PersonalAccountCannotCreateArticle() =>
        Assert.False(AccountStyle.Personal.CanCreateArticle());

    [Theory]
    [InlineData(AccountStyle.Creator)]
    [InlineData(AccountStyle.Journalist)]
    [InlineData(AccountStyle.Business)]
    [InlineData(AccountStyle.Organization)]
    [InlineData(AccountStyle.Agency)]
    public void NonPersonalAccountCanCreateArticle(AccountStyle accountStyle) =>
        Assert.True(accountStyle.CanCreateArticle());
}
