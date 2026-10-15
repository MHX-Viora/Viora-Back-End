using Viora.Application.MiniApps;

namespace viora_BE.Services;

public sealed class DeveloperOrganizationContext(IHttpContextAccessor accessor) : IDeveloperOrganizationContext
{
    public Guid? OrganizationId
    {
        get
        {
            var header = accessor.HttpContext?.Request.Headers["X-Developer-Organization"].ToString();
            if (string.IsNullOrWhiteSpace(header)) return null;
            if (Guid.TryParse(header, out var id) && id != Guid.Empty) return id;
            throw new MiniAppException("INVALID_ORGANIZATION", "Organization context must be a UUID.", 422);
        }
    }
}
