using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppProductionAudit(AppDbContext db)
{
    public void Append(MiniAppProductionHostSession session, MiniAppProductionEventCode code)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Production audit must share the action transaction.");
        if (!Enum.IsDefined(code)) throw new ArgumentOutOfRangeException(nameof(code));
        db.Add(new MiniAppProductionRuntimeEvent
        {
            MiniAppId = session.MiniAppId, HostSessionId = session.Id, AccountId = session.AccountId,
            Code = code, CorrelationId = session.CorrelationId
        });
    }
}
