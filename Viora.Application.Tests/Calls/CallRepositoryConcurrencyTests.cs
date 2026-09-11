using Xunit;

namespace Viora.Application.Tests.Calls;

public sealed class CallRepositoryConcurrencyTests
{
    private static readonly string Source = File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "../../../../Viora.Infrastructure/Persistence/Repositories/CallRepository.cs")));

    [Fact]
    public void TerminalTransitionsUseAStatusGuardedDatabaseUpdate()
    {
        Assert.Contains(
            ".Where(call => call.Id == callId && allowedStatuses.Contains(call.Status))",
            Source);
        Assert.DoesNotContain("ApplyTransitionAsync", Source);
    }

    [Fact]
    public void EndTransitionUsesTheObservedStatusAsItsDatabaseGuard()
    {
        Assert.Contains(
            ".Where(call => call.Id == command.CallId && call.Status == current.Status)",
            Source);
    }
}
