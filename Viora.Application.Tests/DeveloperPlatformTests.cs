using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.MiniApps;
using Xunit;

public sealed class DeveloperPlatformTests
{
    [Fact]
    public async Task ConsentDocumentsUseReadableVietnameseAndVersionedTitles()
    {
        await using var f = await MiniAppHybridIntegrationTests.Fixture.Create(true);
        var terms = Service(f, new Mailer()).GetTerms();
        Assert.Equal(DeveloperPlatformService.TermsVersion, terms.Version);
        Assert.Equal(new[] { "Điều khoản Developer", "Chính sách bảo mật", "Quy định Mini App", "Quy định Open API", "Chính sách dữ liệu người dùng" }, terms.Documents.Select(d => d.Title));
        Assert.All(terms.Documents, d => { Assert.NotEmpty(d.Content); Assert.DoesNotContain("?", d.Content); });
    }
    private sealed class Mailer : IDeveloperVerificationMailer
    {
        public string Code { get; private set; } = "";
        public Task SendCodeAsync(string email,string code,CancellationToken ct) { Code=code; return Task.CompletedTask; }
    }
    private static DeveloperInput Input(int? version=null) => new("Partner",null,"contact@example.com","0901234567",null,null,Country:"VN",Version:version);
    private static DeveloperPlatformService Service(MiniAppHybridIntegrationTests.Fixture f,Mailer mailer) => new(f.Db,mailer,Options.Create(new DeveloperVerificationOptions { OtpHashKey=new string('x',32) }),TimeProvider.System);
    [Fact]
    public async Task SubmissionRequiresVerifiedEmailAndCurrentTermsThenLocksSnapshot()
    {
        await using var f=await MiniAppHybridIntegrationTests.Fixture.Create(true); var mailer=new Mailer(); var s=Service(f,mailer);
        var draft=await s.SaveDraftAsync(f.Other.Id,Input(),default);
        var missing=await Assert.ThrowsAsync<MiniAppException>(()=>s.SubmitAsync(f.Other.Id,new(draft.RegistrationVersion,s.GetTerms().Version,true),default)); Assert.Equal("EMAIL_VERIFICATION_REQUIRED",missing.Code);
        var challenge=await s.RequestEmailAsync(f.Other.Id,new("contact@example.com"),default);
        Assert.DoesNotContain(mailer.Code,(await f.Db.DeveloperEmailVerifications.SingleAsync()).CodeHash);
        await s.ConfirmEmailAsync(f.Other.Id,new(challenge.VerificationId,mailer.Code),default);
        await Assert.ThrowsAsync<MiniAppException>(()=>s.ConfirmEmailAsync(f.Other.Id,new(challenge.VerificationId,mailer.Code),default));
        await Assert.ThrowsAsync<MiniAppException>(()=>s.SubmitAsync(f.Other.Id,new(draft.RegistrationVersion,"old",true),default));
        var submitted=await s.SubmitAsync(f.Other.Id,new(draft.RegistrationVersion,s.GetTerms().Version,true),default); Assert.Equal("PendingReview",submitted.RegistrationStatus);
        await Assert.ThrowsAsync<MiniAppException>(()=>s.SaveDraftAsync(f.Other.Id,Input(submitted.RegistrationVersion) with{Name="Changed"},default));
        var snapshot=await f.Db.DeveloperApplications.SingleAsync(); Assert.Contains("Partner",snapshot.ConfigurationJson); Assert.DoesNotContain("Changed",snapshot.ConfigurationJson);
    }
    [Fact]
    public async Task ReviewsRequireAdminExactVersionAndSeparatePrivateNotes()
    {
        await using var f=await MiniAppHybridIntegrationTests.Fixture.Create(true); var m=new Mailer(); var s=Service(f,m);
        var p=await s.SaveDraftAsync(f.Other.Id,Input(),default); var c=await s.RequestEmailAsync(f.Other.Id,new(p.Email),default); await s.ConfirmEmailAsync(f.Other.Id,new(c.VerificationId,m.Code),default);
        p=await s.SubmitAsync(f.Other.Id,new(p.RegistrationVersion,s.GetTerms().Version,true),default);
        await Assert.ThrowsAsync<MiniAppException>(()=>s.ReviewAsync(f.Other.Id,p.Id,new(p.RegistrationVersion,"Approved"),default));
        f.Owner.Role=AccountRole.Admin; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<MiniAppException>(()=>s.ReviewAsync(f.Owner.Id,p.Id,new(99,"Approved"),default));
        await Assert.ThrowsAsync<MiniAppException>(()=>s.ReviewAsync(f.Owner.Id,p.Id,new(p.RegistrationVersion,"Rejected","  "),default));
        await s.ReviewAsync(f.Owner.Id,p.Id,new(p.RegistrationVersion,"NeedMoreInformation","Need representative","Internal evidence"),default);
        var publicProfile=await s.GetProfileAsync(f.Other.Id,default); Assert.Null(publicProfile!.ReviewHistory!.Single().InternalNotes);
        Assert.Equal("Internal evidence",(await s.GetAdminApplicationAsync(p.Id,default)).ReviewHistory!.Single().InternalNotes);
        var revised=await s.SaveDraftAsync(f.Other.Id,Input(p.RegistrationVersion) with{Name="Revised"},default); Assert.Equal(p.RegistrationVersion+1,revised.RegistrationVersion);
    }
    [Fact]
    public async Task SuspensionRestorationDoesNotApprovePendingProfileUpdate()
    {
        await using var f=await MiniAppHybridIntegrationTests.Fixture.Create(true); var m=new Mailer(); var s=Service(f,m);
        var p=await s.SaveDraftAsync(f.Other.Id,Input(),default); var c=await s.RequestEmailAsync(f.Other.Id,new(p.Email),default); await s.ConfirmEmailAsync(f.Other.Id,new(c.VerificationId,m.Code),default);
        p=await s.SubmitAsync(f.Other.Id,new(p.RegistrationVersion,s.GetTerms().Version,true),default);
        f.Owner.Role=AccountRole.Admin; await f.Db.SaveChangesAsync(); await s.ReviewAsync(f.Owner.Id,p.Id,new(p.RegistrationVersion,"Approved"),default);
        p=await s.SaveDraftAsync(f.Other.Id,Input(p.RegistrationVersion) with{Name="Unapproved change"},default);
        p=await s.SubmitAsync(f.Other.Id,new(p.RegistrationVersion,s.GetTerms().Version,true),default);
        await s.ReviewAsync(f.Owner.Id,p.Id,new(p.RegistrationVersion,"Suspended","Investigation"),default);
        await s.ReviewAsync(f.Owner.Id,p.Id,new(p.RegistrationVersion,"Approved"),default);
        Assert.Equal("Partner",(await f.Db.Developers.SingleAsync(d=>d.Id==p.Id)).Name);
        Assert.Equal("PendingReview",(await s.GetProfileAsync(f.Other.Id,default))!.RegistrationStatus);
    }
    [Fact]
    public async Task ExpiredCodeAndResendCooldownAreEnforced()
    {
        await using var f=await MiniAppHybridIntegrationTests.Fixture.Create(true);var m=new Mailer();var s=Service(f,m);
        var c=await s.RequestEmailAsync(f.Other.Id,new("contact@example.com"),default);
        await Assert.ThrowsAsync<MiniAppException>(()=>s.RequestEmailAsync(f.Other.Id,new("other@example.com"),default));
        var v=await f.Db.DeveloperEmailVerifications.SingleAsync();v.ExpiresAt=DateTime.UtcNow.AddMinutes(-1);await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<MiniAppException>(()=>s.ConfirmEmailAsync(f.Other.Id,new(c.VerificationId,m.Code),default));
    }
    [Fact]
    public async Task BannedAccountAndEmailIdorWrongCodesAndExpiryAreDenied()
    {
        await using var f=await MiniAppHybridIntegrationTests.Fixture.Create(true); var m=new Mailer(); var s=Service(f,m);
        var c=await s.RequestEmailAsync(f.Other.Id,new("contact@example.com"),default);
        await Assert.ThrowsAsync<MiniAppException>(()=>s.ConfirmEmailAsync(f.Owner.Id,new(c.VerificationId,m.Code),default));
        for(var i=0;i<5;i++) await Assert.ThrowsAsync<MiniAppException>(()=>s.ConfirmEmailAsync(f.Other.Id,new(c.VerificationId,"invalid"),default));
        await Assert.ThrowsAsync<MiniAppException>(()=>s.ConfirmEmailAsync(f.Other.Id,new(c.VerificationId,m.Code),default));
        f.Other.Status=AccountStatus.Banned; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<MiniAppException>(()=>s.SaveDraftAsync(f.Other.Id,Input(),default));
    }
}
