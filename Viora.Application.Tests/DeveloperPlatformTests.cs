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
    public async Task SubmissionWithoutEmailVerificationRequiresCurrentTermsThenLocksSnapshot()
    {
        await using var f=await MiniAppHybridIntegrationTests.Fixture.Create(true); var mailer=new Mailer();
        var s=new DeveloperPlatformService(f.Db,mailer,Options.Create(new DeveloperVerificationOptions()),TimeProvider.System);
        var draft=await s.SaveDraftAsync(f.Other.Id,Input(),default);
        Assert.False(s.GetTerms().EmailVerificationRequired);
        await Assert.ThrowsAsync<MiniAppException>(()=>s.SubmitAsync(f.Other.Id,new(draft.RegistrationVersion,s.GetTerms().Version,false),default));
        await Assert.ThrowsAsync<MiniAppException>(()=>s.SubmitAsync(f.Other.Id,new(draft.RegistrationVersion,"old",true),default));
        var submitted=await s.SubmitAsync(f.Other.Id,new(draft.RegistrationVersion,s.GetTerms().Version,true),default); Assert.Equal("PendingReview",submitted.RegistrationStatus);
        Assert.False(submitted.ContactEmailVerified);
        Assert.Null((await f.Db.DeveloperApplications.SingleAsync(a=>a.DeveloperId==draft.Id)).EmailVerifiedAt);
        Assert.Empty(mailer.Code);
        Assert.Empty(await f.Db.DeveloperEmailVerifications.ToListAsync());
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
        var adminHistory = (await s.GetAdminApplicationAsync(p.Id,default)).ReviewHistory!.Single();
        Assert.Equal("Internal evidence",adminHistory.InternalNotes);
        var decisionAudit = await f.Db.MiniAppAuditLogs.SingleAsync(a => a.DeveloperId == p.Id && a.Action == "DeveloperNeedMoreInformation");
        Assert.Equal(adminHistory.Id, decisionAudit.Id);
        Assert.Equal(adminHistory.CreatedAt, decisionAudit.CreatedAt);
        Assert.Equal(f.Owner.Id, adminHistory.ReviewerId);
        Assert.Equal("PendingReview", adminHistory.PreviousStatus);
        Assert.Equal("NeedMoreInformation", adminHistory.Status);
        Assert.Equal(p.RegistrationVersion, adminHistory.Version);
        Assert.Equal("Need representative", adminHistory.Reason);
        Assert.Equal(MiniAppAuditLog.DeveloperReviewEvent, decisionAudit.EventType);
        Assert.Null(decisionAudit.Detail);
        var auditPage = await f.Management.GetAuditLogsAsync(1, 100, null, default);
        Assert.DoesNotContain(auditPage.Items, a => (a.Detail ?? "").Contains("Internal evidence"));
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
        var decisions = (await s.GetAdminApplicationAsync(p.Id, default)).ReviewHistory!;
        Assert.Equal(3, decisions.Count);
        Assert.Equal(2, decisions.Count(d => d.Status == "Approved"));
        Assert.Contains(decisions, d => d.Status == "Suspended" && d.PreviousStatus == "PendingReview" && d.Reason == "Investigation");
    }
    [Fact]
    public async Task TypedHistoryPreservesOriginalIdentifiersAndTimeAndExcludesOrdinaryAuditEvents()
    {
        await using var f = await MiniAppHybridIntegrationTests.Fixture.Create(true);
        var s = Service(f, new Mailer());
        var profile = await s.SaveDraftAsync(f.Other.Id, Input(), default);
        var originalId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var historicalTime = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        f.Db.MiniAppAuditLogs.Add(new()
        {
            Id = originalId, DeveloperId = profile.Id, ActorAccountId = f.Owner.Id,
            EventType = MiniAppAuditLog.DeveloperReviewEvent, Action = "DeveloperRevoked",
            ReviewVersion = 0, ReviewPreviousStatus = "Suspended", ReviewStatus = "Revoked",
            ReviewReason = "Historical reason", ReviewInternalNotes = "Private historical evidence"
        });
        f.Db.MiniAppAuditLogs.Add(new() { DeveloperId = profile.Id, ActorAccountId = f.Owner.Id, Action = "DeveloperRevoked", Detail = "Ordinary activity" });
        await f.Db.SaveChangesAsync();
        await f.Db.MiniAppAuditLogs.Where(a => a.Id == originalId).ExecuteUpdateAsync(setters => setters.SetProperty(a => a.CreatedAt, historicalTime));
        var admin = (await s.GetAdminApplicationAsync(profile.Id, default)).ReviewHistory!.Single();
        Assert.Equal(new DeveloperReviewHistoryDto(originalId, f.Owner.Id, "Suspended", "Revoked", "Historical reason", historicalTime, 0, "Private historical evidence"), admin);
        var owner = (await s.GetProfileAsync(f.Other.Id, default))!.ReviewHistory!.Single();
        Assert.Equal(admin with { InternalNotes = null }, owner);
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
