using System.ComponentModel.DataAnnotations;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Realtime;
using Viora.Domain.Entities;
using Viora.Infrastructure.Realtime;

namespace viora_BE.Controllers;

[ApiController]
[Authorize]
[Route("api/dev/push-test")]
public sealed class DevPushDiagnosticsController(
    IHostEnvironment environment,
    IConfiguration configuration,
    IDeviceTokenRepository deviceTokenRepository,
    IFirebaseMessagingClientFactory firebaseMessagingClientFactory,
    ILogger<DevPushDiagnosticsController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<DevPushTestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<DevPushTestResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<DevPushTestResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<DevPushTestResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Send(
        [FromBody] DevPushTestRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!environment.IsDevelopment() && !configuration.GetValue<bool>("Diagnostics:EnablePushTest"))
            {
                return NotFound(DevPushTestResponse.Fail(
                    firebasePushProjectId: firebaseMessagingClientFactory.ProjectId,
                    tokenSuffix: request?.TokenSuffix,
                    message: "Push diagnostics endpoint is only enabled in Development."));
            }

            if (request is null)
            {
                return BadRequest(DevPushTestResponse.Fail(
                    firebaseMessagingClientFactory.ProjectId,
                    null,
                    "Request body is required."));
            }

            var requestedTokenSuffix = NormalizeTokenSuffix(request.TokenSuffix);
            if (!request.UserId.HasValue && string.IsNullOrWhiteSpace(requestedTokenSuffix))
            {
                return BadRequest(DevPushTestResponse.Fail(
                    firebaseMessagingClientFactory.ProjectId,
                    requestedTokenSuffix,
                    "Provide userId or tokenSuffix."));
            }

            var client = firebaseMessagingClientFactory.CreateClient();
            if (client is null)
            {
                logger.LogError(
                    "Dev push test failed because Firebase app is not configured. FirebaseProjectId: {FirebaseProjectId}.",
                    firebaseMessagingClientFactory.ProjectId ?? "unknown");

                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    DevPushTestResponse.Fail(
                        firebaseMessagingClientFactory.ProjectId,
                        request.TokenSuffix,
                        "Firebase app is not configured."));
            }

            var tokens = request.UserId.HasValue
                ? string.IsNullOrWhiteSpace(requestedTokenSuffix)
                    ? await deviceTokenRepository.GetActiveByUserIdAsync(request.UserId.Value, cancellationToken)
                    : (await deviceTokenRepository.GetActiveByUserIdAsync(request.UserId.Value, cancellationToken))
                        .Where(token => token.Token.EndsWith(requestedTokenSuffix))
                        .ToArray()
                : await deviceTokenRepository.GetActiveByTokenSuffixAsync(requestedTokenSuffix!, cancellationToken);

            var validTokens = tokens
                .Where(token => !string.IsNullOrWhiteSpace(token.Token))
                .ToArray();

            logger.LogInformation(
                "Dev push test token lookup. UserId: {UserId}, RequestedTokenSuffix: {RequestedTokenSuffix}, ActiveTokenCount: {ActiveTokenCount}, ValidTokenCount: {ValidTokenCount}, FirebaseProjectId: {FirebaseProjectId}.",
                request.UserId,
                requestedTokenSuffix,
                tokens.Count,
                validTokens.Length,
                firebaseMessagingClientFactory.ProjectId ?? "unknown");

            foreach (var token in validTokens)
            {
                logger.LogInformation(
                    "Dev push test token candidate. UserId: {UserId}, DeviceId: {DeviceId}, Platform: {Platform}, IsActive: {IsActive}, TokenSuffix: {TokenSuffix}.",
                    token.UserId,
                    token.DeviceId,
                    token.Platform,
                    token.IsActive,
                    GetTokenSuffix(token.Token));
            }

            if (validTokens.Length == 0)
            {
                return NotFound(DevPushTestResponse.Fail(
                    firebaseMessagingClientFactory.ProjectId,
                    requestedTokenSuffix,
                    "No active device token found."));
            }

            var results = new List<DevPushTestTokenResult>();
            foreach (var token in validTokens)
            {
                results.Add(await SendTokenAsync(client, token, request, cancellationToken));
            }

            var first = results.FirstOrDefault();
            var response = new DevPushTestResponse(
                Success: results.All(result => result.Success),
                FirebaseProjectId: firebaseMessagingClientFactory.ProjectId,
                TokenSuffix: first?.TokenSuffix ?? requestedTokenSuffix,
                FirebaseMessageId: results.Count == 1 ? first?.FirebaseMessageId : null,
                MessagingErrorCode: results.Count == 1 ? first?.MessagingErrorCode : null,
                ErrorCode: results.Count == 1 ? first?.ErrorCode : null,
                Message: results.Count == 1 ? first?.Message : null,
                ActiveTokenCount: tokens.Count,
                ValidTokenCount: validTokens.Length,
                Results: results,
                Timestamp: DateTimeOffset.UtcNow);

            return Ok(response);
        }
        catch (Exception exception)
        {
            var firebaseError = ToFirebaseError(exception);
            logger.LogError(
                exception,
                "Dev push test failed before send completed. FirebaseProjectId: {FirebaseProjectId}, TokenSuffix: {TokenSuffix}, MessagingErrorCode: {MessagingErrorCode}, ErrorCode: {ErrorCode}, ErrorType: {ErrorType}, InnerExceptionType: {InnerExceptionType}, InnerExceptionMessage: {InnerExceptionMessage}.",
                firebaseMessagingClientFactory.ProjectId ?? "unknown",
                NormalizeTokenSuffix(request?.TokenSuffix),
                firebaseError.MessagingErrorCode,
                firebaseError.ErrorCode,
                exception.GetType().Name,
                exception.InnerException?.GetType().Name,
                exception.InnerException?.Message);

            return StatusCode(
                StatusCodes.Status502BadGateway,
                DevPushTestResponse.Fail(
                    firebaseMessagingClientFactory.ProjectId,
                    NormalizeTokenSuffix(request?.TokenSuffix),
                    firebaseError.Message,
                    firebaseError.MessagingErrorCode,
                    firebaseError.ErrorCode));
        }
    }

    [HttpGet("ping")]
    [ProducesResponseType<DevPushPingResponse>(StatusCodes.Status200OK)]
    public IActionResult Ping()
    {
        try
        {
            var client = firebaseMessagingClientFactory.CreateClient();
            return Ok(new DevPushPingResponse(
                environment.EnvironmentName,
                configuration.GetValue<bool>("Diagnostics:EnablePushTest"),
                client is not null,
                firebaseMessagingClientFactory.ProjectId,
                null,
                DateTimeOffset.UtcNow));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Dev push ping failed.");
            return Ok(new DevPushPingResponse(
                environment.EnvironmentName,
                configuration.GetValue<bool>("Diagnostics:EnablePushTest"),
                false,
                firebaseMessagingClientFactory.ProjectId,
                exception.Message,
                DateTimeOffset.UtcNow));
        }
    }

    private async Task<DevPushTestTokenResult> SendTokenAsync(
        IFirebaseMessagingClient client,
        DeviceToken token,
        DevPushTestRequest request,
        CancellationToken cancellationToken)
    {
        var suffix = GetTokenSuffix(token.Token);
        var message = new PushMessage(
            token.UserId,
            request.Title ?? "FCM diagnostic",
            request.Body ?? "Viora direct FCM test",
            new Dictionary<string, string>
            {
                ["type"] = "chat",
                ["eventType"] = "message",
                ["conversationId"] = (request.ConversationId ?? Guid.Empty).ToString(),
                ["messageId"] = (request.MessageId ?? Guid.Empty).ToString(),
                ["diagnostic"] = "true"
            });

        try
        {
            var messageId = await client.SendAsync(message, token.Token, cancellationToken);
            logger.LogInformation(
                "Dev push test sent. UserId: {UserId}, DeviceId: {DeviceId}, Platform: {Platform}, TokenSuffix: {TokenSuffix}, FirebaseProjectId: {FirebaseProjectId}, FirebaseMessageId: {FirebaseMessageId}.",
                token.UserId,
                token.DeviceId,
                token.Platform,
                suffix,
                firebaseMessagingClientFactory.ProjectId ?? "unknown",
                messageId);

            return new DevPushTestTokenResult(
                token.UserId,
                token.DeviceId,
                token.Platform,
                suffix,
                true,
                messageId,
                null,
                null,
                "Sent.",
                false);
        }
        catch (FirebasePushTokenInvalidException exception)
        {
            if (exception.ShouldDeactivate)
            {
                await deviceTokenRepository.DeactivateAsync(token.Token, cancellationToken);
            }

            logger.LogWarning(
                exception,
                "Dev push test Firebase messaging error. UserId: {UserId}, DeviceId: {DeviceId}, Platform: {Platform}, TokenSuffix: {TokenSuffix}, FirebaseProjectId: {FirebaseProjectId}, MessagingErrorCode: {MessagingErrorCode}, ErrorCode: {ErrorCode}, HttpStatusCode: {HttpStatusCode}, ShouldDeactivate: {ShouldDeactivate}, Message: {Message}, InnerExceptionType: {InnerExceptionType}, InnerExceptionMessage: {InnerExceptionMessage}.",
                token.UserId,
                token.DeviceId,
                token.Platform,
                suffix,
                firebaseMessagingClientFactory.ProjectId ?? "unknown",
                exception.MessagingErrorCode,
                exception.ErrorCode,
                exception.HttpStatusCode,
                exception.ShouldDeactivate,
                exception.Message,
                exception.InnerException?.GetType().Name,
                exception.InnerException?.Message);

            return new DevPushTestTokenResult(
                token.UserId,
                token.DeviceId,
                token.Platform,
                suffix,
                false,
                null,
                exception.MessagingErrorCode,
                exception.ErrorCode,
                exception.InnerException?.Message ?? exception.Message,
                exception.ShouldDeactivate);
        }
        catch (Exception exception)
        {
            var firebaseError = ToFirebaseError(exception);
            logger.LogError(
                exception,
                "Dev push test send failed. UserId: {UserId}, DeviceId: {DeviceId}, Platform: {Platform}, TokenSuffix: {TokenSuffix}, FirebaseProjectId: {FirebaseProjectId}, MessagingErrorCode: {MessagingErrorCode}, ErrorCode: {ErrorCode}, ErrorType: {ErrorType}, InnerExceptionType: {InnerExceptionType}, InnerExceptionMessage: {InnerExceptionMessage}.",
                token.UserId,
                token.DeviceId,
                token.Platform,
                suffix,
                firebaseMessagingClientFactory.ProjectId ?? "unknown",
                firebaseError.MessagingErrorCode,
                firebaseError.ErrorCode,
                exception.GetType().Name,
                exception.InnerException?.GetType().Name,
                exception.InnerException?.Message);

            return new DevPushTestTokenResult(
                token.UserId,
                token.DeviceId,
                token.Platform,
                suffix,
                false,
                null,
                firebaseError.MessagingErrorCode,
                firebaseError.ErrorCode,
                firebaseError.Message,
                false);
        }
    }

    private static FirebaseErrorDiagnostics ToFirebaseError(Exception exception)
    {
        if (exception is FirebaseMessagingException messagingException)
        {
            return new FirebaseErrorDiagnostics(
                messagingException.MessagingErrorCode?.ToString(),
                messagingException.ErrorCode.ToString(),
                messagingException.Message);
        }

        if (exception is FirebaseException firebaseException)
        {
            return new FirebaseErrorDiagnostics(
                null,
                firebaseException.ErrorCode.ToString(),
                firebaseException.Message);
        }

        return new FirebaseErrorDiagnostics(null, exception.GetType().Name, exception.Message);
    }

    private static string GetTokenSuffix(string token) =>
        token.Length <= 8 ? token : token[^8..];

    private static string? NormalizeTokenSuffix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return GetTokenSuffix(trimmed);
    }

    private sealed record FirebaseErrorDiagnostics(string? MessagingErrorCode, string? ErrorCode, string Message);
}

public sealed class DevPushTestRequest
{
    public Guid? UserId { get; init; }

    [StringLength(4096, MinimumLength = 4)]
    public string? TokenSuffix { get; init; }

    public string? Title { get; init; }
    public string? Body { get; init; }
    public Guid? ConversationId { get; init; }
    public Guid? MessageId { get; init; }
}

public sealed record DevPushPingResponse(
    string EnvironmentName,
    bool IsEnabled,
    bool IsFirebaseConfigured,
    string? FirebaseProjectId,
    string? ErrorMessage,
    DateTimeOffset Timestamp);

public sealed record DevPushTestResponse(
    bool Success,
    string? FirebaseProjectId,
    string? TokenSuffix,
    string? FirebaseMessageId,
    string? MessagingErrorCode,
    string? ErrorCode,
    string? Message,
    int ActiveTokenCount,
    int ValidTokenCount,
    IReadOnlyList<DevPushTestTokenResult> Results,
    DateTimeOffset Timestamp)
{
    public static DevPushTestResponse Fail(
        string? firebasePushProjectId,
        string? tokenSuffix,
        string message,
        string? messagingErrorCode = null,
        string? errorCode = null) =>
        new(
            false,
            firebasePushProjectId,
            tokenSuffix,
            null,
            messagingErrorCode,
            errorCode,
            message,
            0,
            0,
            [],
            DateTimeOffset.UtcNow);
}

public sealed record DevPushTestTokenResult(
    Guid UserId,
    string? DeviceId,
    DevicePlatform Platform,
    string TokenSuffix,
    bool Success,
    string? FirebaseMessageId,
    string? MessagingErrorCode,
    string? ErrorCode,
    string? Message,
    bool Deactivated);
