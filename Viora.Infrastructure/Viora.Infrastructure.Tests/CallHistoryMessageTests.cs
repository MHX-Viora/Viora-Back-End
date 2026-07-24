using Viora.Application.Calls;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class CallHistoryMessageTests
{
    [Theory]
    [InlineData(CallType.Audio, CallStatus.Ended, 65, "Cuộc gọi thoại • 01:05")]
    [InlineData(CallType.Video, CallStatus.Ended, 3723, "Cuộc gọi video • 01:02:03")]
    [InlineData(CallType.Audio, CallStatus.Rejected, 0, "Cuộc gọi thoại bị từ chối")]
    [InlineData(CallType.Video, CallStatus.Cancelled, 0, "Cuộc gọi video đã hủy")]
    [InlineData(CallType.Video, CallStatus.Missed, 0, "Cuộc gọi video nhỡ")]
    public void Format_describes_terminal_call(
        CallType callType,
        CallStatus status,
        int duration,
        string expected)
    {
        Assert.Equal(expected, CallHistoryMessages.Format(callType, status, duration));
    }
}
