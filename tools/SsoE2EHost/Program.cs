using System.Net;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;
using viora_BE.Sso;

// This executable cannot load application settings/connection strings. All identities,
// domain proofs, keys and storage belong to this one loopback-only fixture instance.
const string issuer="https://127.0.0.1:5490/";
var directory=Path.Combine(Path.GetTempPath(),"ankt-oidc-browser-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
using var certificates=new FixtureCertificates(directory);
WebApplication? app=null;
try{
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development",Args=[]});
builder.Configuration.Sources.Clear();builder.Logging.ClearProviders();
builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{
    ["Sso:Enabled"]="true",["Sso:Issuer"]=issuer,["Sso:FirstPartyOrigin"]=issuer.TrimEnd('/'),
    ["Sso:SigningCertificatePath"]=certificates.SigningPath,["Sso:EncryptionCertificatePath"]=certificates.EncryptionPath
});
builder.WebHost.ConfigureKestrel(o=>o.Listen(IPAddress.Loopback,5490,listen=>listen.UseHttps(certificates.Tls)));
builder.Services.AddDbContext<AppDbContext>(o=>{
    var connection=new SqliteConnection("Data Source="+Path.Combine(directory,"fixture.sqlite"));
    connection.CreateFunction<string?,int>("char_length",v=>v?.Length??0);o.UseSqlite(connection,contextOwnsConnection:true);
});
builder.Services.AddSingleton(Options.Create(new MiniAppOptions{ActiveKeyVersion="fixture",EncryptionKeys=new(){["fixture"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}}));
builder.Services.AddSingleton<MiniAppSecretProtector>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>{
    o.MapInboundClaims=false;o.TokenValidationParameters=new(){ValidIssuer=issuer,ValidAudience="isolated-social",IssuerSigningKey=new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32))};
});
builder.Services.AddAuthorization();builder.Services.AddControllers().AddApplicationPart(typeof(SsoAuthorizationController).Assembly);
builder.Services.AddAnktSso(builder.Configuration);builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddCors(o=>o.AddPolicy("Web",p=>p.WithOrigins(issuer.TrimEnd('/')).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
app=builder.Build();
Guid accountId,socialId;var confidentialSecret="cs_"+Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
using(var scope=app.Services.CreateScope()){
    var services=scope.ServiceProvider;var db=services.GetRequiredService<AppDbContext>();await db.Database.EnsureCreatedAsync();
    var developer=new DeveloperAccount{FullName="Isolated partner",EmailNormalized="isolated@example.test",Status=DeveloperStatus.Active,TermsAcceptedAt=DateTime.UtcNow};
    var account=new Account{Email="isolated-user@example.test",Status=AccountStatus.Active};accountId=account.Id;socialId=Guid.NewGuid();db.Accounts.Add(account);
    db.RefreshTokens.Add(new(){AccountId=accountId,SessionId=socialId,TokenHash=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),ExpiresAt=DateTime.UtcNow.AddHours(1)});
    foreach(var (id,type,origin) in new[]{("public","Public","https://localhost:5491"),("confidential","Confidential","https://127.0.0.2:5492")}){
        var mini=new MiniApp{Developer=developer,CreateRequestId=Guid.NewGuid(),AppId="app_e2e_"+id,Name="ANKT "+type+" example",Category="utilities",SsoEnabled=true,WebsiteUrl="https://partner.example.com",Status=MiniAppStatus.Draft};
        var client=new MiniAppOAuthClient{MiniApp=mini,ClientKey="e2e-"+id,ClientType=type,SandboxEnabled=true};db.MiniAppOAuthClients.Add(client);
        db.MiniAppRedirectUris.Add(new(){MiniApp=mini,RedirectUri=origin+"/callback"});
        db.MiniAppDomainVerifications.Add(new(){MiniApp=mini,VerificationFileName="fixture-"+id+".txt",Hostname="partner.example.com",Status=MiniAppDomainStatus.Verified,ExpiresAt=DateTime.UtcNow.AddHours(1)});
        if(type=="Confidential"){
            var encrypted=services.GetRequiredService<MiniAppSecretProtector>().Encrypt(confidentialSecret,client.Id);
            db.MiniAppClientSecrets.Add(new(){OAuthClient=client,SecretEncrypted=encrypted.Ciphertext,SecretKeyVersion=encrypted.Version});
        }
        await db.SaveChangesAsync();await using var tx=await db.Database.BeginTransactionAsync();
        await services.GetRequiredService<MiniAppOidcRegistry>().SynchronizeAsync(mini.Id,default);await tx.CommitAsync();
    }
}
await File.WriteAllTextAsync(Path.Combine(directory,"fixture.json"),JsonSerializer.Serialize(new{issuer,publicOrigin="https://localhost:5491",confidentialOrigin="https://127.0.0.2:5492",publicClientId="e2e-public",confidentialClientId="e2e-confidential",confidentialSecret}));
app.UseRouting();app.UseCors("Web");app.UseAuthentication();app.UseRateLimiter();app.UseAuthorization();app.UseAntiforgery();app.MapControllers();app.MapSsoSessionEndpoints();
string Page(string body)=>"<!doctype html><html lang=vi><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><link rel=icon href='data:,'><title>ANKT isolated OIDC fixture</title><main><h1>ANKT isolated OIDC fixture</h1>"+body+"</main></html>";
string Csrf(HttpContext http,IAntiforgery csrf){var tokens=csrf.GetAndStoreTokens(http);return "<input type=hidden name='"+HtmlEncoder.Default.Encode(tokens.FormFieldName)+"' value='"+HtmlEncoder.Default.Encode(tokens.RequestToken!)+"'>";}
app.Use(async(http,next)=>{http.Response.Headers.CacheControl="no-store";http.Response.Headers["Referrer-Policy"]="no-referrer";await next();});
app.MapGet("/sso/login",(Guid transaction,HttpContext http,IAntiforgery csrf)=>Results.Content(Page("<p>Người dùng ANKT cục bộ đã được seed riêng cho kiểm thử. Không dùng tài khoản thật.</p><form method=post action='/fixture/login'>"+Csrf(http,csrf)+"<input type=hidden name=transaction value='"+transaction+"'><button>Tiếp tục với người dùng fixture</button></form>"),"text/html; charset=utf-8"));
app.MapPost("/fixture/login",async(HttpContext http,IAntiforgery csrf,SsoBootstrapService bootstrap)=>{
    await csrf.ValidateRequestAsync(http);var form=await http.Request.ReadFormAsync();if(!Guid.TryParse(form["transaction"],out var transaction))return Results.BadRequest();
    await bootstrap.AuthenticateAsync(transaction,accountId,socialId,http.RequestAborted);return Results.Redirect("/sso/complete?transaction="+transaction);
});
app.MapGet("/",(HttpContext http,IAntiforgery csrf)=>Results.Content(Page("<p>Chỉ dùng loopback, domain proof được seed; không chứng minh quyền kiểm soát domain công khai.</p><p><a href='https://localhost:5491/'>Public SPA</a> · <a href='https://127.0.0.2:5492/'>Confidential backend</a></p>"+string.Join("",new[]{"public","confidential"}.Select(id=>"<form method=post action='/fixture/revoke/"+id+"'>"+Csrf(http,csrf)+"<button>Thu hồi quyền "+id+"</button></form>"))),"text/html; charset=utf-8"));
app.MapPost("/fixture/revoke/{id}",async(string id,HttpContext http,IAntiforgery csrf,SsoAuthorizationOwnerService grants)=>{
    await csrf.ValidateRequestAsync(http);var authentication=await http.AuthenticateAsync(SsoSessionHandler.SchemeName);
    if(!authentication.Succeeded||authentication.Principal!.FindFirst("sub")?.Value!=accountId.ToString())return Results.Unauthorized();
    if(id is not ("public" or "confidential"))return Results.BadRequest();
    foreach(var grant in (await grants.ListAsync(accountId,http.RequestAborted)).Where(g=>g.AppId=="app_e2e_"+id&&g.RevokedAt==null))await grants.RevokeAsync(accountId,grant.Id,http.RequestAborted);
    return Results.Redirect("/");
});
Console.WriteLine("Isolated OIDC issuer: "+issuer);Console.WriteLine("Partner fixture directory: "+directory);
await app.RunAsync();}
finally{
    if(app!=null)await app.DisposeAsync();SqliteConnection.ClearAllPools();
    var allowed=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))+Path.DirectorySeparatorChar;
    if(!Path.GetFullPath(directory).StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(directory).StartsWith("ankt-oidc-browser-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected fixture cleanup directory.");
    Directory.Delete(directory,true);
}
