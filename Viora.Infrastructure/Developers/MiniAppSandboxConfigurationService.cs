using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSandboxConfigurationService(AppDbContext db,MiniAppSandboxAccess access,
    IPublicWebFetcher fetcher,IPublicJsonFetcher jsonFetcher,MiniAppSandboxOptions options,TimeProvider clock,
    MiniAppSsoService? sso=null,MiniAppSandboxEvidenceReader? evidence=null)
{
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    private sealed record Snapshot(MiniApp App,MiniAppOAuthClient? Client,MiniAppDomainVerification? Proof,string[] Redirects,string Fingerprint);
    private static readonly SandboxCheckKey[] SsoChecks=[SandboxCheckKey.Callbacks,SandboxCheckKey.SsoClient,SandboxCheckKey.Discovery,
        SandboxCheckKey.Consent,SandboxCheckKey.Token,SandboxCheckKey.UserInfo];
    private static readonly SandboxCheckKey[] LaunchChecks=[SandboxCheckKey.Website,SandboxCheckKey.Domain,SandboxCheckKey.Environment,
        SandboxCheckKey.Callbacks,SandboxCheckKey.SsoClient,SandboxCheckKey.Discovery];
    private async Task<Snapshot> LoadAsync(Guid appId,CancellationToken ct)
    {
        var app=await db.MiniApps.AsNoTracking().Include(x=>x.Developer).SingleOrDefaultAsync(x=>x.Id==appId&&x.DeletedAt==null,ct)
            ?? throw MiniAppSandboxAccess.NotFound();
        var client=await db.MiniAppOAuthClients.AsNoTracking().SingleOrDefaultAsync(x=>x.MiniAppId==appId&&x.Environment==MiniAppOAuthEnvironment.Sandbox&&x.RevokedAt==null,ct);
        var hostname=MiniAppDomainLifecycle.Host(app.WebsiteUrl);
        var proof=await db.MiniAppDomainVerifications.AsNoTracking().SingleOrDefaultAsync(x=>x.MiniAppId==appId&&x.Hostname==hostname&&x.RevokedAt==null,ct);
        var redirects=await db.MiniAppRedirectUris.AsNoTracking().Where(x=>x.MiniAppId==appId&&x.Environment==MiniAppOAuthEnvironment.Sandbox)
            .OrderBy(x=>x.RedirectUri).Select(x=>x.RedirectUri).ToArrayAsync(ct);
        var website=app.WebsiteUrl??"";try{website=PublicWebAddress.Normalize(website);}catch(DeveloperAuthException){}
        return new(app,client,proof,redirects,MiniAppSandboxSessionService.Fingerprint(app,client,proof,website,redirects));
    }
    public async Task<SandboxCheckExecution> ExecuteAsync(Guid appId,SandboxCheckKey key,CancellationToken ct)=>
        await EvaluateAsync(await LoadAsync(appId,ct),key,ct);

    public async Task<SandboxReadinessDto> ReadinessAsync(SandboxActor actor,string appId,CancellationToken ct)
    {
        var app=await access.RequireAsync(actor,appId,ct);var snapshot=await LoadAsync(app.Id,ct);var started=Now;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var checks=new List<SandboxReadinessCheck>();
        try
        {
            foreach(var key in Enum.GetValues<SandboxCheckKey>())
            {
                var result=await EvaluateAsync(snapshot,key,deadline.Token);
                checks.Add(new(Wire(key),Applicable(snapshot,key),result.Status.ToString(),MiniAppSandboxDiagnosticCatalog.Code(result.MessageCode),
                    MiniAppSandboxDiagnosticCatalog.Source(result.EvidenceSource),result.EvidenceAt));
            }
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested)
        {throw new DeveloperAuthException(504,"SANDBOX_CHECK_TIMEOUT","Kiểm tra cấu hình quá thời gian cho phép.");}
        await access.RequireAsync(actor,appId,ct);
        var current=await LoadAsync(app.Id,ct);
        if(snapshot.Fingerprint!=current.Fingerprint || snapshot.Proof is { } proof && proof.ExpiresAt>started && proof.ExpiresAt<=Now)
            throw new DeveloperAuthException(409,"SANDBOX_CONFIGURATION_CHANGED","Cấu hình vừa thay đổi. Hãy kiểm tra lại.");
        // Credentials can expire and the library projection can change during network I/O.
        if(snapshot.App.SsoEnabled)
        {
            var result=await EvaluateAsync(current,SandboxCheckKey.SsoClient,ct);
            checks[checks.FindIndex(x=>x.Key=="ssoClient")]=new("ssoClient",true,result.Status.ToString(),
                MiniAppSandboxDiagnosticCatalog.Code(result.MessageCode),MiniAppSandboxDiagnosticCatalog.Source(result.EvidenceSource),result.EvidenceAt);
        }
        Guid? pinnedRun=null;
        if(evidence!=null){
            pinnedRun=await evidence.LatestRunIdAsync(app.Id,snapshot.Fingerprint,ct);
            foreach(var key in Enum.GetValues<SandboxCheckKey>().Where(MiniAppSandboxEvidenceReader.Handles)){
                if(!Applicable(snapshot,key))continue;
                var observed=await evidence.LatestAsync(app.Id,snapshot.Fingerprint,key,ct,pinnedRun);
                checks[checks.FindIndex(x=>x.Key==Wire(key))]=new(Wire(key),true,observed.Status.ToString(),MiniAppSandboxDiagnosticCatalog.Code(observed.MessageCode),MiniAppSandboxDiagnosticCatalog.Source(observed.EvidenceSource),observed.EvidenceAt);
            }
            if(await evidence.LatestRunIdAsync(app.Id,snapshot.Fingerprint,ct)!=pinnedRun)throw new DeveloperAuthException(409,"SANDBOX_EVIDENCE_CHANGED","Lượt kiểm tra vừa thay đổi. Hãy kiểm tra readiness lại.");
        }
        bool Passed(SandboxReadinessCheck check)=>!check.Applicable||check.Status==nameof(SandboxCheckStatus.Passed);
        var configuration=checks.Where(x=>Enum.TryParse<SandboxCheckKey>(x.Key,true,out var key)&&key<=SandboxCheckKey.Discovery).All(Passed);
        var launch=checks.Where(x=>LaunchChecks.Any(key=>Wire(key)==x.Key)).All(Passed);
        var review=checks.All(Passed);
        if(review&&evidence!=null&&(pinnedRun==null||!await evidence.IsFreshAsync(pinnedRun.Value,snapshot.Fingerprint,ct)||
            checks.Where(x=>x.Applicable&&Enum.TryParse<SandboxCheckKey>(x.Key,true,out var key)&&MiniAppSandboxEvidenceReader.Handles(key))
                .Any(x=>x.EvidenceAt==null||x.EvidenceAt<Now.AddMinutes(-5))))
            throw new DeveloperAuthException(409,"SANDBOX_EVIDENCE_CHANGED","Bằng chứng kiểm tra không còn hiệu lực.");
        return new("Sandbox",snapshot.Fingerprint,configuration,launch,review,Now,checks.ToArray(),
            checks.Where(x=>!Passed(x)).Select(x=>x.Key).ToArray(),checks.Where(x=>x.Status=="Warning").Select(x=>x.Key).ToArray());
    }
    private static string Wire(SandboxCheckKey key)=>char.ToLowerInvariant(key.ToString()[0])+key.ToString()[1..];
    private static bool Applicable(Snapshot snapshot,SandboxCheckKey key)=>snapshot.App.SsoEnabled||!SsoChecks.Contains(key);
    private SandboxCheckExecution Result(bool passed)=>new(passed?SandboxCheckStatus.Passed:SandboxCheckStatus.Failed,
        passed?SandboxMessageCode.CheckPassed:SandboxMessageCode.CheckFailed,SandboxEvidenceSource.Server,Now);
    private async Task<SandboxCheckExecution> EvaluateAsync(Snapshot snapshot,SandboxCheckKey key,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(!Applicable(snapshot,key))return new(SandboxCheckStatus.Skipped,SandboxMessageCode.CheckNotApplicable,SandboxEvidenceSource.Server);
        var app=snapshot.App;
        try
        {
            return key switch
            {
                SandboxCheckKey.Logo=>Result(!string.IsNullOrWhiteSpace(app.LogoUrl)),
                SandboxCheckKey.Name=>Result(!string.IsNullOrWhiteSpace(app.Name)),
                SandboxCheckKey.Category=>Result(MiniAppInput.Categories.Contains(app.Category)),
                SandboxCheckKey.Description=>Result(!string.IsNullOrWhiteSpace(app.ShortDescription)&&!string.IsNullOrWhiteSpace(app.Description)),
                SandboxCheckKey.SupportEmail=>Result(ValidEmail(app.SupportEmail)),
                SandboxCheckKey.Website=>Result(await PublicPageAsync(app.WebsiteUrl,ct)),
                SandboxCheckKey.PrivacyPolicy=>Result(await PublicPageAsync(app.PrivacyPolicyUrl,ct)),
                SandboxCheckKey.TermsOfService=>Result(await PublicPageAsync(app.TermsOfServiceUrl,ct)),
                SandboxCheckKey.Domain=>Result(snapshot.Proof is {Status:MiniAppDomainStatus.Verified,RevokedAt:null} proof&&
                    proof.Hostname==MiniAppDomainLifecycle.Host(app.WebsiteUrl)&&proof.ExpiresAt>Now),
                SandboxCheckKey.Environment=>Result(snapshot.Client is {Environment:MiniAppOAuthEnvironment.Sandbox,SandboxEnabled:true,RevokedAt:null,ClientType:"Public" or "Confidential"}&&
                    app.Developer.Status==DeveloperStatus.Active&&app.Status is not (MiniAppStatus.Suspended or MiniAppStatus.Rejected)&&ValidPartner(app.WebsiteUrl)),
                SandboxCheckKey.Callbacks=>Result(snapshot.Redirects.Length>0&&snapshot.Redirects.All(ValidCallback)),
                SandboxCheckKey.SsoClient=>Result(await SsoClientAsync(snapshot,ct)),
                SandboxCheckKey.Discovery=>Result(await DiscoveryAsync(ct)),
                _=>new(SandboxCheckStatus.NotImplemented,SandboxMessageCode.MissingEvidence,SandboxEvidenceSource.Server)
            };
        }
        catch(Exception error) when(error is DeveloperAuthException or PublicFetchException or JsonException or FormatException or CryptographicException)
        {return Result(false);}
    }
    private bool ValidPartner(string? website)
    {
        var origin=new Uri(PublicWebAddress.Normalize(website)).GetLeftPart(UriPartial.Authority);
        return !new[]{options.PortalOrigin,options.HostOrigin,options.Issuer}.Any(x=>new Uri(x).GetLeftPart(UriPartial.Authority)==origin);
    }
    private static bool ValidEmail(string? email){try{DeveloperInput.Email(email);return true;}catch(DeveloperAuthException){return false;}}
    private static bool ValidCallback(string value)
    {
        try{MiniAppInput.Url(value,false,true);var uri=new Uri(value);return uri.Scheme=="https"&&uri.Query.Length==0&&uri.Fragment.Length==0;}
        catch(DeveloperAuthException){return false;}
    }
    private async Task<bool> PublicPageAsync(string? value,CancellationToken ct)
    {var uri=new Uri(PublicWebAddress.Normalize(value));await fetcher.FetchAsync(uri,256*1024,false,ct);return true;}
    private async Task<bool> SsoClientAsync(Snapshot snapshot,CancellationToken ct)
    {
        if(sso==null||snapshot.Client==null)return false;
        var registry=await sso.Get(snapshot.App.DeveloperAccountId,snapshot.App.AppId,ct);
        return registry.Clients.Any(x=>x.Environment=="Sandbox"&&x.ClientId==snapshot.Client.ClientKey&&x.Ready);
    }
    private async Task<bool> DiscoveryAsync(CancellationToken ct)
    {
        var issuer=new Uri(PublicWebAddress.Normalize(options.Issuer));
        using var discovery=await JsonAsync(new Uri(issuer,".well-known/openid-configuration"),ct);var doc=discovery.RootElement;
        static string? Text(JsonElement element,string name)=>element.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
        static bool Supports(JsonElement element,string name,string expected)=>element.TryGetProperty(name,out var values)&&values.ValueKind==JsonValueKind.Array&&values.EnumerateArray().Any(x=>x.ValueKind==JsonValueKind.String&&x.GetString()==expected);
        if(doc.ValueKind!=JsonValueKind.Object||Text(doc,"issuer")!=issuer.AbsoluteUri||
            Text(doc,"authorization_endpoint")!=new Uri(issuer,"/connect/authorize").AbsoluteUri||
            Text(doc,"token_endpoint")!=new Uri(issuer,"/connect/token").AbsoluteUri||Text(doc,"userinfo_endpoint")!=new Uri(issuer,"/connect/userinfo").AbsoluteUri||
            Text(doc,"jwks_uri")!=new Uri(issuer,"/.well-known/jwks.json").AbsoluteUri||
            !Supports(doc,"response_types_supported","code")||!Supports(doc,"grant_types_supported","authorization_code")||
            !Supports(doc,"code_challenge_methods_supported","S256")||!Supports(doc,"id_token_signing_alg_values_supported","RS256"))return false;
        // Fetch only the known server endpoint, never an arbitrary URL supplied by discovery.
        using var jwks=await JsonAsync(new Uri(issuer,"/.well-known/jwks.json"),ct);var root=jwks.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("keys",out var keys)||keys.ValueKind!=JsonValueKind.Array||keys.GetArrayLength() is <1 or >16)return false;
        var ids=new HashSet<string>(StringComparer.Ordinal);var signing=false;
        foreach(var key in keys.EnumerateArray())
        {
            if(key.ValueKind!=JsonValueKind.Object||new[]{"d","p","q","dp","dq","qi","oth","k"}.Any(x=>key.TryGetProperty(x,out _)))return false;
            var kid=Text(key,"kid");if(string.IsNullOrWhiteSpace(kid)||kid.Length>128||!ids.Add(kid))return false;
            if(Text(key,"kty")!="RSA"||Text(key,"use") is not (null or "sig")||Text(key,"alg") is not (null or "RS256"))continue;
            if(key.TryGetProperty("key_ops",out var operations) && (operations.ValueKind!=JsonValueKind.Array||
                !operations.EnumerateArray().Any(x=>x.ValueKind==JsonValueKind.String&&x.GetString()=="verify")))continue;
            var modulus=Text(key,"n");var exponent=Text(key,"e");if(modulus==null||exponent==null)return false;
            if(modulus.Length is <342 or >1366 || exponent.Length is <1 or >8)return false;
            using var rsa=RSA.Create();rsa.ImportParameters(new(){Modulus=WebEncoders.Base64UrlDecode(modulus),Exponent=WebEncoders.Base64UrlDecode(exponent)});
            if(rsa.KeySize<2048)return false;signing=true;
        }
        return signing;
    }
    private async Task<JsonDocument> JsonAsync(Uri uri,CancellationToken ct)
    {
        var response=await jsonFetcher.FetchJsonAsync(uri,64*1024,ct);
        var document=JsonDocument.Parse(response.Content,new JsonDocumentOptions {MaxDepth=8});
        static bool Unique(JsonElement element)=>element.ValueKind switch
        {
            JsonValueKind.Object=>element.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()==element.EnumerateObject().Count()&&element.EnumerateObject().All(x=>Unique(x.Value)),
            JsonValueKind.Array=>element.EnumerateArray().All(Unique),_=>true
        };
        if(Unique(document.RootElement))return document;
        document.Dispose();throw new JsonException();
    }
}
