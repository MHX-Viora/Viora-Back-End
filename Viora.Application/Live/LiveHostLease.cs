using Viora.Domain.Entities;

namespace Viora.Application.Live;

public sealed class LiveLifecycleOptions
{
    public int HostTimeoutSeconds { get; set; } = 120;
    public int PreparingTimeoutSeconds { get; set; } = 600;
    public int SweepIntervalSeconds { get; set; } = 30;
}

public static class LiveHostLease
{
    public static bool IsStale(LiveStatus status, DateTime? lastSeenAt, DateTime? startedAt,
        DateTime createdAt, DateTime now, LiveLifecycleOptions options)
    {
        if (status == LiveStatus.Preparing)
            return now - (lastSeenAt ?? createdAt) > TimeSpan.FromSeconds(options.PreparingTimeoutSeconds);
        if (status is LiveStatus.Live or LiveStatus.Reconnecting)
            return now - (lastSeenAt ?? startedAt ?? createdAt) > TimeSpan.FromSeconds(options.HostTimeoutSeconds);
        return false;
    }
}
