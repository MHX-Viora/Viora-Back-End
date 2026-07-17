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

        var registerRoutes = methods[nameof(DeviceTokensController.Register)]
            .GetCustomAttributes<HttpPostAttribute>()
            .Select(attribute => attribute.Template)
            .ToArray();
        Assert.Contains("register", registerRoutes);
        Assert.Contains("~/api/device/register", registerRoutes);

        var unregisterRoutes = methods[nameof(DeviceTokensController.Unregister)]
            .GetCustomAttributes<HttpPostAttribute>()
            .Select(attribute => attribute.Template)
            .ToArray();
        Assert.Contains("unregister", unregisterRoutes);
        Assert.Contains("~/api/device/unregister", unregisterRoutes);
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
        AssertProperties<DeviceTokenResponse>("Success", "IsActive", "Message");
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
        var events = typeof(RealtimeEvents)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!);

        foreach (var eventName in new[]
        {
            "ReceiveNotification",
            "ReceiveMessage",
            "ConversationCreated",
            "ConversationUpdated",
            "NewMessageNotification",
            "ConversationRead",
            "MessageRead",
            "MessageDelivered",
            "MessageDeleted",
            "MessageUpdated",
            "ReactionAdded",
            "ReactionRemoved",
            "TypingStarted",
            "TypingStopped",
            "UserOnline",
            "UserOffline",
            "ConversationPinned",
            "ConversationMuted",
            "ConversationRenamed",
            "ConversationAvatarChanged",
            "MemberAdded",
            "MemberRemoved",
            "MemberLeft"
        })
        {
            Assert.Equal(eventName, events[eventName]);
        }
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
