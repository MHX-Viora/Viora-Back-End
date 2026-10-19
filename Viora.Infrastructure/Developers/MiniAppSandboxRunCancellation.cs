namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSandboxRunCancellation
{
    public const int Capacity=64;
    private readonly object gate=new();
    private readonly Dictionary<Guid,(Guid Session,CancellationTokenSource Source)> active=[];
    public CancellationTokenSource Register(Guid runId,Guid sessionId,CancellationToken ct)
    {
        lock(gate)
        {
            if(active.Count>=Capacity||active.ContainsKey(runId))throw new InvalidOperationException("Sandbox worker capacity exceeded.");
            var source=CancellationTokenSource.CreateLinkedTokenSource(ct);active.Add(runId,(sessionId,source));return source;
        }
    }
    public void Release(Guid runId,CancellationTokenSource source)
    {
        lock(gate){if(active.TryGetValue(runId,out var value)&&ReferenceEquals(value.Source,source))active.Remove(runId);}
        source.Dispose();
    }
    public void Cancel(Guid runId)
    {
        CancellationTokenSource? source;lock(gate)source=active.TryGetValue(runId,out var value)?value.Source:null;
        CancelSource(source);
    }
    public void CancelSession(Guid sessionId)
    {
        CancellationTokenSource[] sources;lock(gate)sources=active.Values.Where(x=>x.Session==sessionId).Select(x=>x.Source).ToArray();
        foreach(var source in sources)CancelSource(source);
    }
    private static void CancelSource(CancellationTokenSource? source)
    {try{source?.Cancel();}catch(ObjectDisposedException){/* Execution finished concurrently. */}}
}
