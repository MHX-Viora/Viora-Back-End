using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Viora.Domain.Entities;
using viora_BE.Controllers;
using viora_BE.Controllers.Admin;
using Xunit;

namespace Viora.Application.Tests.Advertisements;

public sealed class AdvertisementRequestValidationTests
{
    [Fact]
    public void Create_body_validates_record_constructor_parameters()
    {
        using var services = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider();
        var context = NewContext(services);
        var start = DateTime.UtcNow;
        var body = new CreateAdvertisementBody(Guid.NewGuid(), AdvertisementObjective.Awareness, null,
            AdvertisementCtaType.LearnMore, AdvertisementTargetingMode.Automatic, null, null, null,
            50_000m, 49_999m, start, start.AddDays(1));

        services.GetRequiredService<IObjectModelValidator>().Validate(context, null, string.Empty, body);

        Assert.False(context.ModelState.IsValid);
        Assert.True(context.ModelState.ContainsKey(nameof(CreateAdvertisementBody.TotalBudget)));
    }

    [Fact]
    public void Other_positional_request_bodies_do_not_throw_during_validation()
    {
        using var services = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider();
        var validator = services.GetRequiredService<IObjectModelValidator>();

        foreach (var body in new object[]
        {
            new AdvertisementEventBody("event-12345678"),
            new AdvertisementFeedbackBody(AdvertisementFeedbackType.Hide, null),
            new RejectAdvertisementBody("Nội dung không phù hợp"),
            new SetForgotPasswordPhoneRequest(Guid.NewGuid(), null, null),
            new ResetForgottenPasswordRequest(null, null, null),
            new AcceptLegalDocumentRequest(Guid.NewGuid(), "v1", null, null)
        })
        {
            var context = NewContext(services);
            validator.Validate(context, null, string.Empty, body);
            Assert.True(context.ModelState.IsValid);
        }
    }

    private static ActionContext NewContext(IServiceProvider services) =>
        new(new DefaultHttpContext { RequestServices = services }, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
}
