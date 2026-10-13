using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
namespace Viora.Infrastructure.MiniApps;

public sealed class DeveloperPlatformService(AppDbContext db,IDeveloperVerificationMailer mailer,IOptions<DeveloperVerificationOptions> options,TimeProvider clock) : IDeveloperPlatformService
{
    public const string TermsVersion="2026-10-13.1";
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    public DeveloperTermsDto GetTerms()=>new(TermsVersion,[
        new("developer","Điều khoản Developer","Cung cấp thông tin chính xác; chịu trách nhiệm ứng dụng, thành viên và bảo mật thông tin tích hợp. ANKT có thể yêu cầu bổ sung, từ chối, tạm khóa hoặc thu hồi quyền khi vi phạm; không tự động xóa dữ liệu."),
        new("privacy","Chính sách bảo mật","ANKT lưu thông tin liên hệ, hồ sơ, phiên bản đã nộp và lịch sử xét duyệt để vận hành nền tảng. Không nộp giấy tờ nhạy cảm khi chưa có yêu cầu và chính sách lưu giữ cụ thể. Email liên hệ không yêu cầu xác minh khi đăng ký Developer; người đăng ký chịu trách nhiệm cung cấp địa chỉ chính xác."),
        new("mini-app","Quy định Mini App","Ứng dụng phải có thông tin hỗ trợ và chính sách bảo mật, nội dung hợp pháp, URL an toàn. Thay đổi domain, callback, quyền hoặc chức năng quan trọng phải được xét duyệt trước khi phát hành."),
        new("open-api","Quy định Open API","Chỉ dùng endpoint và quyền đã công bố hoặc duyệt; lưu secret và token trên backend; dùng OAuth/OIDC và PKCE. Không dùng JWT hoặc API nội bộ ANKT; không ghi secret hoặc token vào log."),
        new("user-data","Chính sách dữ liệu người dùng","Chỉ nhận dữ liệu trong phạm vi consent còn hiệu lực; không tự liên kết tài khoản theo email. Tôn trọng thu hồi quyền, không bán hoặc sử dụng dữ liệu ngoài mục đích đã công bố.")]);
    private async Task ActiveAccount(Guid id,CancellationToken ct)
    {
        if(!await db.Accounts.AnyAsync(x=>x.Id==id&&x.Status==AccountStatus.Active&&x.DeletedAt==null,ct)) throw new MiniAppException("ACCOUNT_RESTRICTED","Active account required.",403);
    }
    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch(DbUpdateConcurrencyException) { throw new MiniAppException("CONCURRENT_UPDATE","Profile changed. Reload and retry.",409); }
        catch(DbUpdateException) { throw new MiniAppException("CONFLICT","Profile or membership already exists. Reload and retry.",409); }
    }
    private static string Email(string value)
    {
        var normalized=value.Trim().ToLowerInvariant();
        if(normalized.Length>255||!MailAddress.TryCreate(normalized,out var parsed)||parsed.Address!=normalized) throw new MiniAppException("VALIDATION_ERROR","Valid contact email required.",422);
        return normalized;
    }
    private static DeveloperInput Normalize(DeveloperInput x)
    {
        if(string.IsNullOrWhiteSpace(x.Name)||x.Name.Length>120||x.PartnerType is not ("Individual" or "Business" or "Organization")||string.IsNullOrWhiteSpace(x.Country)||x.Country.Length>80||string.IsNullOrWhiteSpace(x.Phone)||x.Phone.Length>20||x.CompanyName?.Length>160||x.LegalName?.Length>200||x.TradingName?.Length>200||x.BusinessRegistrationNumber?.Length>80||x.Address?.Length>500||x.RepresentativeName?.Length>120)
            throw new MiniAppException("VALIDATION_ERROR","Partner type, name, phone and country are required; field lengths must be valid.",422);
        if(x.PartnerType!="Individual"&&(string.IsNullOrWhiteSpace(x.LegalName)||string.IsNullOrWhiteSpace(x.Address)||string.IsNullOrWhiteSpace(x.RepresentativeName)||string.IsNullOrWhiteSpace(x.Website))) throw new MiniAppException("VALIDATION_ERROR","Legal name, address, website and representative are required for organizations.",422);
        if(!string.IsNullOrWhiteSpace(x.Website)&&(!Uri.TryCreate(x.Website,UriKind.Absolute,out var website)||!MiniAppSecurityPolicy.IsAllowedHttpsUrl(x.Website,[website.IdnHost]))) throw new MiniAppException("VALIDATION_ERROR","Public HTTPS website required.",422);
        return x with{Name=x.Name.Trim(),Email=Email(x.Email),Phone=x.Phone.Trim(),Country=x.Country.Trim(),AccountId=null,Version=null};
    }
    private static void Apply(Developer d,DeveloperInput x)
    {
        d.Name=x.Name;d.Email=x.Email;d.CompanyName=x.CompanyName?.Trim();d.Phone=x.Phone;d.Website=x.Website?.Trim();d.PartnerType=x.PartnerType;d.LegalName=x.LegalName?.Trim();d.TradingName=x.TradingName?.Trim();d.BusinessRegistrationNumber=x.BusinessRegistrationNumber?.Trim();d.Country=x.Country;d.Address=x.Address?.Trim();d.RepresentativeName=x.RepresentativeName?.Trim();
    }
    private static DeveloperInput From(Developer d)=>new(d.Name,d.CompanyName,d.Email,d.Phone,d.Website,null,d.PartnerType,d.LegalName,d.TradingName,d.BusinessRegistrationNumber,d.Country,d.Address,d.RepresentativeName);
    private static string RegistryStatus(Developer d)=>d.Status switch {DeveloperStatus.Pending=>"PendingReview",DeveloperStatus.Active=>"Approved",_=>d.Status.ToString()};
    private async Task<DeveloperProfileDto> Profile(Developer d,Guid? accountId,bool admin,CancellationToken ct)
    {
        var application=await db.DeveloperApplications.AsNoTracking().Where(a=>a.DeveloperId==d.Id&&a.Version==d.RegistrationVersion).SingleOrDefaultAsync(ct);
        var x=d.DraftApplicationJson is not null?JsonSerializer.Deserialize<DeveloperInput>(d.DraftApplicationJson)!:application is not null?JsonSerializer.Deserialize<DeveloperInput>(application.ConfigurationJson)!:From(d);
        var role=accountId==d.AccountId?"Owner":await db.DeveloperMemberships.Where(m=>m.DeveloperId==d.Id&&m.AccountId==accountId).Select(m=>m.Role.ToString()).SingleOrDefaultAsync(ct)??"Viewer";
        var verified=await db.DeveloperEmailVerifications.AnyAsync(v=>v.AccountId==d.AccountId&&v.Email==x.Email&&v.VerifiedAt!=null&&v.DeliverySucceeded,ct);
        var histories=await db.MiniAppAuditLogs.AsNoTracking().Where(h=>h.DeveloperId==d.Id&&h.EventType==MiniAppAuditLog.DeveloperReviewEvent).OrderByDescending(h=>h.CreatedAt).ThenByDescending(h=>h.Id).Take(100).ToListAsync(ct);
        return new(d.Id,d.AccountId,x.Name,x.CompanyName,x.Email,x.Phone,x.Website,d.Status.ToString(),await db.MiniApps.CountAsync(a=>a.DeveloperId==d.Id&&a.DeletedAt==null,ct),d.CreatedAt,d.UpdatedAt,role,x.PartnerType,x.LegalName,x.TradingName,x.BusinessRegistrationNumber,x.Country,x.Address,x.RepresentativeName,d.RegistrationStatus=="Legacy"?RegistryStatus(d):d.RegistrationStatus,d.RegistrationVersion,application?.SubmittedAt,histories.FirstOrDefault()?.ReviewReason,verified,application?.TermsVersion,application?.TermsAcceptedAt,histories.Select(h=>new DeveloperReviewHistoryDto(h.Id,h.ActorAccountId!.Value,h.ReviewPreviousStatus!,h.ReviewStatus!,h.ReviewReason,h.CreatedAt,h.ReviewVersion!.Value,admin?h.ReviewInternalNotes:null)).ToArray());
    }
    public async Task<DeveloperProfileDto?> GetProfileAsync(Guid accountId,CancellationToken ct)
    {
        await ActiveAccount(accountId,ct);
        var d=await db.Developers.AsNoTracking().SingleOrDefaultAsync(d=>d.AccountId==accountId||db.DeveloperMemberships.Any(m=>m.DeveloperId==d.Id&&m.AccountId==accountId),ct);
        return d is null?null:await Profile(d,accountId,false,ct);
    }
    public async Task<DeveloperProfileDto> SaveDraftAsync(Guid accountId,DeveloperInput input,CancellationToken ct)
    {
        await ActiveAccount(accountId,ct); var x=Normalize(input);
        var d=await db.Developers.SingleOrDefaultAsync(d=>d.AccountId==accountId,ct);
        if(d is null)
        {
            if(await db.DeveloperMemberships.AnyAsync(m=>m.AccountId==accountId,ct)) throw new MiniAppException("MEMBERSHIP_EXISTS","Account belongs to another developer.",409);
            if(await db.Developers.AnyAsync(d=>d.Email==x.Email,ct)) throw new MiniAppException("DEVELOPER_EXISTS","Contact email already registered.",409);
            d=new Developer{AccountId=accountId,Status=DeveloperStatus.Draft,RegistrationVersion=1,RegistrationStatus="Draft"}; Apply(d,x);db.Developers.Add(d);db.DeveloperMemberships.Add(new(){DeveloperId=d.Id,AccountId=accountId,Role=DeveloperMembershipRole.Owner});
        }
        else
        {
            if(input.Version!=d.RegistrationVersion) throw new MiniAppException("CONCURRENT_UPDATE","Expected registration version required.",409);
            if(d.Status is DeveloperStatus.Suspended or DeveloperStatus.Revoked) throw new MiniAppException("DEVELOPER_RESTRICTED","Developer access restricted.",403);
            if(d.RegistrationStatus=="PendingReview"||d.RegistrationStatus=="Legacy"&&d.Status==DeveloperStatus.Pending) throw new MiniAppException("REVIEW_PENDING","Submitted profile cannot be edited.",409);
            if(d.RegistrationStatus!="Draft") d.RegistrationVersion++;
            if(d.Status!=DeveloperStatus.Active) { Apply(d,x); d.Status=DeveloperStatus.Draft; }
            d.RegistrationStatus="Draft";
        }
        d.DraftApplicationJson=JsonSerializer.Serialize(x);d.ConcurrencyStamp=Guid.NewGuid(); Audit(accountId,d.Id,"DeveloperDraftSaved");await Save(ct);return await Profile(d,accountId,false,ct);
    }
    public async Task<DeveloperProfileDto> SubmitAsync(Guid accountId,DeveloperSubmitInput input,CancellationToken ct)
    {
        await ActiveAccount(accountId,ct);var d=await db.Developers.SingleOrDefaultAsync(d=>d.AccountId==accountId,ct)??throw new MiniAppException("OWNER_REQUIRED","Owner required.",403);
        if(d.RegistrationVersion!=input.Version||d.RegistrationStatus!="Draft"||d.DraftApplicationJson is null) throw new MiniAppException("CONCURRENT_UPDATE","Current draft version required.",409);
        if(!input.AcceptedTerms||input.TermsVersion!=TermsVersion) throw new MiniAppException("TERMS_REQUIRED","Accept the current Developer terms.",422);
        var x=Normalize(JsonSerializer.Deserialize<DeveloperInput>(d.DraftApplicationJson)!);
        if(await db.Developers.AnyAsync(other=>other.Id!=d.Id&&other.Email==x.Email,ct)) throw new MiniAppException("DEVELOPER_EXISTS","Contact email already registered.",409);
        var verifiedAt=await db.DeveloperEmailVerifications.Where(v=>v.AccountId==accountId&&v.Email==x.Email&&v.VerifiedAt!=null&&v.DeliverySucceeded).OrderByDescending(v=>v.VerifiedAt).Select(v=>v.VerifiedAt).FirstOrDefaultAsync(ct);
        db.DeveloperApplications.Add(new(){DeveloperId=d.Id,Version=d.RegistrationVersion,ConfigurationJson=JsonSerializer.Serialize(x),TermsVersion=TermsVersion,TermsAcceptedAt=Now,SubmittedAt=Now,SubmittedByAccountId=accountId,EmailVerifiedAt=verifiedAt});
        d.DraftApplicationJson=null;d.RegistrationStatus="PendingReview";d.ConcurrencyStamp=Guid.NewGuid();if(d.Status!=DeveloperStatus.Active)d.Status=DeveloperStatus.Pending;
        Audit(accountId,d.Id,"DeveloperSubmitted"); await Notify(accountId,"Đã gửi hồ sơ Developer","Hồ sơ đang chờ xét duyệt.",ct);await Save(ct);return await Profile(d,accountId,false,ct);
    }
    private string Hash(Guid account,string email,Guid id,string code)
    {
        var key=options.Value.OtpHashKey;if(Encoding.UTF8.GetByteCount(key)<32)throw new MiniAppException("EMAIL_DELIVERY_UNAVAILABLE","Verification key has not been configured.",503);
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key),Encoding.UTF8.GetBytes($"{account:N}|{email}|{id:N}|{code}")));
    }
    public async Task<DeveloperEmailChallenge> RequestEmailAsync(Guid accountId,DeveloperEmailRequest input,CancellationToken ct)
    {
        await ActiveAccount(accountId,ct);var email=Email(input.Email);var now=Now;
        if(await db.DeveloperEmailVerifications.AnyAsync(v=>v.AccountId==accountId&&v.CreatedAt>now.AddSeconds(-60),ct))throw new MiniAppException("RATE_LIMITED","Wait 60 seconds before requesting another email.",429);
        var code=RandomNumberGenerator.GetInt32(0,1000000).ToString("D6");var v=new DeveloperEmailVerification{AccountId=accountId,Email=email,ExpiresAt=now.AddMinutes(10),RequestWindow=new DateTimeOffset(now).ToUnixTimeSeconds()/60};v.CodeHash=Hash(accountId,email,v.Id,code);
        db.DeveloperEmailVerifications.Add(v);await Save(ct);
        try { await mailer.SendCodeAsync(email,code,ct); }
        catch(MiniAppException) { throw; }
        catch(Exception e) when(e is not OperationCanceledException) { throw new MiniAppException("EMAIL_DELIVERY_FAILED","Could not send verification email.",503); }
        v.DeliverySucceeded=true;await Save(ct);return new(v.Id,v.ExpiresAt,60);
    }
    public async Task<DeveloperEmailConfirmed> ConfirmEmailAsync(Guid accountId,DeveloperEmailConfirm input,CancellationToken ct)
    {
        await ActiveAccount(accountId,ct);
        var v=await db.DeveloperEmailVerifications.SingleOrDefaultAsync(v=>v.Id==input.VerificationId&&v.AccountId==accountId,ct)??throw new MiniAppException("INVALID_VERIFICATION","Verification unavailable.",422);
        if(!v.DeliverySucceeded||v.VerifiedAt!=null||v.ExpiresAt<=Now||v.Attempts>=5)throw new MiniAppException("INVALID_VERIFICATION","Verification expired, used or locked.",422);
        v.Attempts++;
        var valid=input.Code.Length==6&&input.Code.All(char.IsAsciiDigit)&&CryptographicOperations.FixedTimeEquals(Convert.FromHexString(v.CodeHash),Convert.FromHexString(Hash(accountId,v.Email,v.Id,input.Code)));
        if(valid)v.VerifiedAt=Now;await Save(ct);
        if(!valid)throw new MiniAppException("INVALID_VERIFICATION","Incorrect verification code.",422);
        return new(true,v.Email,v.VerifiedAt!.Value);
    }
    public async Task<DeveloperProfileDto> GetAdminApplicationAsync(Guid id,CancellationToken ct)
    {
        var d=await db.Developers.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==id,ct)??throw new MiniAppException("DEVELOPER_NOT_FOUND","Developer not found.",404);return await Profile(d,d.AccountId,true,ct);
    }
    public async Task<DeveloperProfileDto> ReviewAsync(Guid actorAccountId,Guid id,DeveloperReviewInput input,CancellationToken ct)
    {
        if(!await db.Accounts.AnyAsync(a=>a.Id==actorAccountId&&a.Role==AccountRole.Admin&&a.Status==AccountStatus.Active&&a.DeletedAt==null,ct))throw new MiniAppException("ADMIN_REQUIRED","Active administrator required.",403);
        var d=await db.Developers.SingleOrDefaultAsync(d=>d.Id==id,ct)??throw new MiniAppException("DEVELOPER_NOT_FOUND","Developer not found.",404);
        if(d.AccountId==actorAccountId)throw new MiniAppException("SELF_REVIEW_DENIED","Cannot review your own developer application.",403);
        if(input.Version!=d.RegistrationVersion)throw new MiniAppException("CONCURRENT_UPDATE","Reviewed version changed.",409);
        if(input.Reason?.Length>1000||input.InternalNotes?.Length>2000||input.Status!="Approved"&&string.IsNullOrWhiteSpace(input.Reason))throw new MiniAppException("VALIDATION_ERROR","Reason required; note lengths must be valid.",422);
        var before=d.RegistrationStatus=="Legacy"?RegistryStatus(d):d.RegistrationStatus;
        var application=await db.DeveloperApplications.SingleOrDefaultAsync(a=>a.DeveloperId==id&&a.Version==input.Version,ct);
        var reviewPending=before=="PendingReview";
        var allowed=input.Status switch {"Approved"=>reviewPending||d.Status==DeveloperStatus.Suspended,"Rejected" or "NeedMoreInformation"=>reviewPending,"Suspended"=>d.Status==DeveloperStatus.Active,"Revoked"=>d.Status is DeveloperStatus.Active or DeveloperStatus.Suspended,_=>false};
        if(!allowed)throw new MiniAppException("INVALID_STATUS_TRANSITION","Developer transition not allowed.",409);
        if(input.Status=="Approved")
        {
            if(d.AccountId is not Guid owner||!await db.Accounts.AnyAsync(a=>a.Id==owner&&a.Status==AccountStatus.Active&&a.DeletedAt==null,ct))throw new MiniAppException("ACCOUNT_RESTRICTED","Developer owner must be active.",403);
            if(reviewPending&&input.Version>0&&application is null)throw new MiniAppException("APPLICATION_REQUIRED","Submitted snapshot required.",409);
            if(application is not null && reviewPending)Apply(d,JsonSerializer.Deserialize<DeveloperInput>(application.ConfigurationJson)!);
            d.Status=DeveloperStatus.Active;
        }
        else if(input.Status=="Suspended")d.Status=DeveloperStatus.Suspended;
        else if(input.Status=="Revoked")d.Status=DeveloperStatus.Revoked;
        else if(d.Status!=DeveloperStatus.Active)d.Status=input.Status=="Rejected"?DeveloperStatus.Rejected:DeveloperStatus.NeedMoreInformation;
        if(application is not null&&reviewPending&&input.Status is "Approved" or "Rejected" or "NeedMoreInformation")application.Status=input.Status;
        d.RegistrationStatus=input.Status=="Approved"&&!reviewPending ? d.DraftApplicationJson is not null ? "Draft" : application?.Status=="PendingReview" ? "PendingReview" : "Approved" : input.Status;d.ConcurrencyStamp=Guid.NewGuid();
        db.MiniAppAuditLogs.Add(new(){EventType=MiniAppAuditLog.DeveloperReviewEvent,DeveloperId=id,ReviewVersion=input.Version,ActorAccountId=actorAccountId,Action="Developer"+input.Status,ReviewPreviousStatus=before,ReviewStatus=input.Status,ReviewReason=input.Reason?.Trim(),ReviewInternalNotes=input.InternalNotes?.Trim()});
        if(d.AccountId is Guid recipient)await Notify(recipient,"Hồ sơ Developer: "+input.Status,input.Reason?.Trim()??"Hồ sơ đã được duyệt.",ct);await Save(ct);return await Profile(d,d.AccountId,true,ct);
    }
    private void Audit(Guid actor,Guid developer,string action)=>db.MiniAppAuditLogs.Add(new(){ActorAccountId=actor,DeveloperId=developer,Action=action});
    private async Task Notify(Guid accountId,string title,string content,CancellationToken ct)
    {
        var userId=await db.Users.Where(u=>u.AccountId==accountId).Select(u=>(Guid?)u.Id).SingleOrDefaultAsync(ct);
        if(userId is Guid id)db.Notifications.Add(new(){UserId=id,NotificationType=NotificationType.System,Title=title,Content=content});
    }
}
