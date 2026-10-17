using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSsoService(AppDbContext db, MiniAppSecretProtector protector, TimeProvider clock,
    MiniAppOidcRegistry? registry = null, IOpenIddictApplicationManager? applications = null)
{
    private async Task<MiniApp> Own(Guid owner,string appId,CancellationToken ct) => await db.MiniApps
        .SingleOrDefaultAsync(x=>x.AppId==appId && x.DeveloperAccountId==owner && x.DeletedAt==null,ct)
        ?? throw new DeveloperAuthException(404,"MINI_APP_NOT_FOUND","Không tìm thấy Mini App.");
    private static MiniAppOAuthEnvironment Environment(string value) => value switch {
        "Sandbox"=>MiniAppOAuthEnvironment.Sandbox,"Production"=>MiniAppOAuthEnvironment.Production,
        _=>throw MiniAppInput.Invalid("Môi trường phải là Sandbox hoặc Production.")};

    public async Task<MiniAppSsoDto> Get(Guid owner,string appId,CancellationToken ct)
    {
        var app=await Own(owner,appId,ct);
        var clients=await db.MiniAppOAuthClients.AsNoTracking().Where(x=>x.MiniAppId==app.Id && x.RevokedAt==null).OrderBy(x=>x.Environment).ToListAsync(ct);
        var output=new List<MiniAppSsoClientDto>();
        foreach(var client in clients)
        {
            var redirects=await db.MiniAppRedirectUris.AsNoTracking().Where(x=>x.MiniAppId==app.Id && x.Environment==client.Environment)
                .OrderBy(x=>x.RedirectUri).Select(x=>x.RedirectUri).ToArrayAsync(ct);
            string? code=null;
            try
            {
                await new SsoClientReader(db,clock).EligibleAsync(client.ClientKey,client.AllowedScopes.Split(' ',StringSplitOptions.RemoveEmptyEntries),client.Environment.ToString(),ct);
                if(registry==null || applications==null) code="SSO_RUNTIME_DISABLED";
                else
                {
                    var projection=await applications.FindByClientIdAsync(client.ClientKey,ct);
                    var expected=new HashSet<string>(StringComparer.Ordinal){OpenIddictConstants.Permissions.Endpoints.Authorization,
                        OpenIddictConstants.Permissions.Endpoints.Token,OpenIddictConstants.Permissions.Endpoints.Revocation,
                        OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,OpenIddictConstants.Permissions.ResponseTypes.Code};
                    if(client.ClientType=="Confidential") expected.Add(OpenIddictConstants.Permissions.Endpoints.Introspection);
                    foreach(var scope in client.AllowedScopes.Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(x=>x!="openid"))
                        expected.Add(OpenIddictConstants.Permissions.Prefixes.Scope+scope);
                    if(projection==null || await applications.GetClientTypeAsync(projection,ct)!=client.ClientType.ToLowerInvariant() ||
                        !expected.SetEquals(await applications.GetPermissionsAsync(projection,ct)) ||
                        !(await applications.GetRedirectUrisAsync(projection,ct)).Order(StringComparer.Ordinal).SequenceEqual(redirects.Order(StringComparer.Ordinal)) ||
                        !(await applications.GetRequirementsAsync(projection,ct)).Contains(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange)) code="SSO_PROJECTION_STALE";
                    if(redirects.Length==0) code="SSO_REDIRECT_REQUIRED";
                    else if(redirects.Any(uri=>!Uri.TryCreate(uri,UriKind.Absolute,out var parsed)||parsed.Scheme!="https"||parsed.UserInfo.Length!=0||parsed.Fragment.Length!=0||uri.Contains('*'))) code="SSO_REDIRECT_INVALID";
                }
            }
            catch(DeveloperAuthException error){code=error.Code;}
            output.Add(new(client.ClientKey,client.ClientType,client.Environment.ToString(),client.SandboxEnabled,
                client.AllowedScopes.Split(' ',StringSplitOptions.RemoveEmptyEntries),redirects,client.Version,code==null,code));
        }
        return new(app.AppId,app.SsoEnabled,app.Version,output);
    }

    public async Task<MiniAppSsoDto> Save(Guid owner,string appId,string environment,MiniAppSsoRequest request,CancellationToken ct)
    {
        var env=Environment(environment);
        if(request.ClientType is not ("Public" or "Confidential") || request.Scopes==null || request.Scopes.Length>3 ||
            !request.Scopes.Contains("openid",StringComparer.Ordinal) || request.Scopes.Any(x=>!SsoClientPolicy.SupportedScopes.Contains(x,StringComparer.Ordinal)) ||
            request.RedirectUris==null || request.RedirectUris.Length is <1 or >20) throw MiniAppInput.Invalid("Cấu hình SSO không hợp lệ.");
        var redirects=request.RedirectUris.Select(x=>new Uri(MiniAppInput.Url(x,false,true),UriKind.Absolute).AbsoluteUri).Distinct(StringComparer.Ordinal).ToArray();
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        var app=await Own(owner,appId,ct);
        var client=await db.MiniAppOAuthClients.SingleOrDefaultAsync(x=>x.MiniAppId==app.Id && x.Environment==env,ct);
        if(app.Version!=request.AppVersion || client?.Version!=request.ClientVersion) throw Conflict();
        if(client?.RevokedAt!=null) throw new DeveloperAuthException(409,"SSO_CLIENT_REVOKED","Client đã bị thu hồi.");
        if(client==null)
        {
            client=new(){MiniAppId=app.Id,Environment=env,ClientKey="mc_"+WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)),ClientType=request.ClientType};
            db.MiniAppOAuthClients.Add(client);
        }
        var credentials=await db.MiniAppClientSecrets.Where(x=>x.OAuthClientId==client.Id && x.RevokedAt==null).ToListAsync(ct);
        var now=clock.GetUtcNow().UtcDateTime;
        if(request.ClientType=="Public") foreach(var credential in credentials) credential.RevokedAt=now;
        else if(!credentials.Any(x=>x.ExpiresAt==null || x.ExpiresAt>now))
        {
            foreach(var credential in credentials) credential.RevokedAt=now;
            var encrypted=protector.Encrypt("cs_"+WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48)),client.Id);
            db.MiniAppClientSecrets.Add(new(){OAuthClientId=client.Id,SecretEncrypted=encrypted.Ciphertext,SecretKeyVersion=encrypted.Version});
        }
        client.ClientType=request.ClientType;client.AllowedScopes=string.Join(' ',request.Scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        client.SandboxEnabled=env==MiniAppOAuthEnvironment.Sandbox && request.SandboxEnabled;client.Version=Guid.NewGuid();
        app.SsoEnabled=request.SsoEnabled;app.Version=Guid.NewGuid();
        await db.MiniAppRedirectUris.Where(x=>x.MiniAppId==app.Id && x.Environment==env).ExecuteDeleteAsync(ct);
        db.MiniAppRedirectUris.AddRange(redirects.Select(uri=>new MiniAppRedirectUri{MiniAppId=app.Id,Environment=env,RedirectUri=uri}));
        db.MiniAppAuditLogs.Add(new(){MiniAppId=app.Id,ActorDeveloperId=owner,Action="sso.configuration.updated"});
        await db.SaveChangesAsync(ct);
        if(registry!=null) await registry.SynchronizeAsync(app.Id,ct);
        await transaction.CommitAsync(ct);
        return await Get(owner,appId,ct);
    }
    public async Task<string> Credential(Guid owner,string appId,string environment,CancellationToken ct)
    {
        var app=await Own(owner,appId,ct);var env=Environment(environment);var now=clock.GetUtcNow().UtcDateTime;
        var client=await db.MiniAppOAuthClients.SingleOrDefaultAsync(x=>x.MiniAppId==app.Id && x.Environment==env && x.RevokedAt==null,ct);
        if(client?.ClientType!="Confidential") throw MiniAppInput.Invalid("Chỉ Confidential Client có credential backend.");
        var secret=await db.MiniAppClientSecrets.SingleOrDefaultAsync(x=>x.OAuthClientId==client.Id && x.RevokedAt==null && (x.ExpiresAt==null || x.ExpiresAt>now),ct)
            ?? throw new DeveloperAuthException(409,"SSO_CREDENTIAL_UNAVAILABLE","Credential không còn hiệu lực.");
        var plaintext=protector.Decrypt(secret.SecretEncrypted,secret.SecretKeyVersion,client.Id);
        db.MiniAppAuditLogs.Add(new(){MiniAppId=app.Id,ActorDeveloperId=owner,Action="sso.credential.viewed"});
        await db.SaveChangesAsync(ct);return plaintext;
    }
    public async Task<MiniAppSsoCredentialDto> RotateCredential(Guid owner,string appId,string environment,Guid clientVersion,CancellationToken ct)
    {
        var env=Environment(environment);await using var transaction=await db.Database.BeginTransactionAsync(ct);
        var app=await Own(owner,appId,ct);
        var client=await db.MiniAppOAuthClients.SingleOrDefaultAsync(x=>x.MiniAppId==app.Id && x.Environment==env && x.RevokedAt==null,ct);
        if(client?.ClientType!="Confidential")throw MiniAppInput.Invalid("Chỉ Confidential Client có credential backend.");
        if(client.Version!=clientVersion)throw Conflict();
        await db.MiniAppClientSecrets.Where(x=>x.OAuthClientId==client.Id && x.RevokedAt==null)
            .ExecuteUpdateAsync(x=>x.SetProperty(p=>p.RevokedAt,clock.GetUtcNow().UtcDateTime),ct);
        var plaintext="cs_"+WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));var encrypted=protector.Encrypt(plaintext,client.Id);
        db.MiniAppClientSecrets.Add(new(){OAuthClientId=client.Id,SecretEncrypted=encrypted.Ciphertext,SecretKeyVersion=encrypted.Version});
        app.Version=Guid.NewGuid();client.Version=Guid.NewGuid();
        db.MiniAppAuditLogs.Add(new(){MiniAppId=app.Id,ActorDeveloperId=owner,Action="sso.credential.rotated"});
        await db.SaveChangesAsync(ct);if(registry!=null)await registry.SynchronizeAsync(app.Id,ct);
        await transaction.CommitAsync(ct);return new(client.ClientKey,plaintext,client.Version);
    }
    private static DeveloperAuthException Conflict()=>new(409,"SSO_CONFIGURATION_CONFLICT","Cấu hình vừa thay đổi. Hãy tải lại trước khi lưu.");
}
