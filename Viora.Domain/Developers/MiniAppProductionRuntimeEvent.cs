using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

public enum MiniAppProductionEventCode { LaunchGranted, Redeemed, Heartbeat, Closed, Invalidated, BridgeReady, BridgeUnavailable, PermissionDenied }

// No free-text payload, URLs, tokens, claims, exception strings or user content.
public sealed class MiniAppProductionRuntimeEvent : CreatedEntity
{
    public Guid MiniAppId { get; set; }
    public Guid? HostSessionId { get; set; }
    public Guid? AccountId { get; set; }
    public MiniAppProductionEventCode Code { get; set; }
    public Guid CorrelationId { get; set; }
}
