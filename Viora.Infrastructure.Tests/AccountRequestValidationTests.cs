using System.ComponentModel.DataAnnotations;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class AccountRequestValidationTests
{
    [Theory]
    [InlineData(typeof(RegisterAccountRequest))]
    [InlineData(typeof(LoginAccountRequest))]
    [InlineData(typeof(UpdateAccountRequest))]
    public void Record_validation_metadata_is_declared_on_constructor_parameters(Type requestType)
    {
        var constructor = Assert.Single(requestType.GetConstructors());

        Assert.All(
            constructor.GetParameters(),
            parameter => Assert.NotEmpty(parameter.GetCustomAttributes(typeof(ValidationAttribute), inherit: true)));
    }
}
