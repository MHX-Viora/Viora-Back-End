using System.ComponentModel.DataAnnotations;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Application.Tests.Wallets;

public sealed class WalletRequestValidationTests
{
    [Theory]
    [InlineData(typeof(DepositBody))]
    [InlineData(typeof(BankAccountBody))]
    [InlineData(typeof(WithdrawalBody))]
    public void Positional_record_validation_metadata_is_defined_on_constructor_parameters(Type requestType)
    {
        var constructor = Assert.Single(requestType.GetConstructors());

        foreach (var parameter in constructor.GetParameters())
        {
            var property = requestType.GetProperty(parameter.Name!,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);
            Assert.NotNull(property);

            var propertyValidation = property.GetCustomAttributes(typeof(ValidationAttribute), inherit: true);
            var parameterValidation = parameter.GetCustomAttributes(typeof(ValidationAttribute), inherit: true);

            Assert.Empty(propertyValidation);
            if (parameter.Name is not ("IsDefault" or "BankAccountId"))
                Assert.NotEmpty(parameterValidation);
        }
    }
}
