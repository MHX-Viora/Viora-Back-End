using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Notifications;
using Viora.Application.Realtime;
using Viora.Domain.Entities;
using Viora.Infrastructure.Realtime;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class RealtimeApiContractTests
{
    [Fact]
    public void Device_tokens_controller_exposes_authenticated_routes()
    {
        Assert.NotNull(typeof(DeviceTokensController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/device-token", typeof(DeviceTokensController).GetCustomAttribute<RouteAttribute>()!.Template);

        var methods = typeof(DeviceTokensController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(DeviceTokensController))
            .ToDictionary(method => method.Name);

        Assert.Equal("register", methods[nameof(DeviceTokensController.Register)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("unregister", methods[nameof(DeviceTokensController.Unregister)].GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void Realtime_hub_requires_authorization()
    {
        Assert.NotNull(typeof(RealtimeHub).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void Device_token_contracts_match_client_shape()
    {
        AssertProperties<RegisterDeviceTokenRequest>("AppVersion", "DeviceId", "DeviceName", "Platform", "Token");
        AssertProperties<UnregisterDeviceTokenRequest>("Token");
        AssertProperties<DeviceTokenResponse>("Success", "IsActive");
    }

    [Fact]
    public void Realtime_notification_payload_matches_notification_item_contract()
    {
        AssertProperties<NotificationItemResponse>(
            "Id",
            "Type",
            "Title",
            "Content",
            "ImageUrl",
            "IsRead",
            "CreatedAt",
            "Sender",
            "Reference");
    }

    [Fact]
    public void Realtime_events_include_core_events()
    {
        Assert.Equal("ReceiveNotification", RealtimeEvents.ReceiveNotification);
        Assert.Equal("ReceiveMessage", RealtimeEvents.ReceiveMessage);
        Assert.Equal("TypingStarted", RealtimeEvents.TypingStarted);
        Assert.Equal("UserOffline", RealtimeEvents.UserOffline);
    }

    [Fact]
    public void Device_platform_includes_mobile_and_web()
    {
        Assert.True(Enum.IsDefined(DevicePlatform.Android));
        Assert.True(Enum.IsDefined(DevicePlatform.Ios));
        Assert.True(Enum.IsDefined(DevicePlatform.Web));
    }

    private static void AssertProperties<T>(params string[] names)
    {
        var properties = typeof(T).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(names.Order().ToArray(), properties);
    }
}
