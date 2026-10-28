using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppVersionMutationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyEditorConcurrencyIsReportedAsConflict(bool wrappedTransient)
    {
        var filter = new viora_BE.Developer.MiniAppConflictFilter();
        Exception error = wrappedTransient ? new InvalidOperationException("provider-transient-fixture",
            new Npgsql.PostgresException("serialization-fixture", "ERROR", "ERROR", Npgsql.PostgresErrorCodes.SerializationFailure))
            : new DbUpdateConcurrencyException("fixture-stale-app-revision");
        var result = await filter.InvokeAsync(null!, _ => throw error);
        Assert.Equal(409, Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task MetadataCallbacksIntegrationAndSandboxSsoKeepOneWorkingDraftAndFrozenHistory()
    {
        await using var host = await MiniAppApiTests.Host.Create(review: true); var http = host.Client;
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", await host.Login("version-mutation@example.test"));
        var app = (await (await http.PostAsJsonAsync("/api/developer/mini-apps", new MiniAppCreateRequest("Before edits", "utilities", Guid.NewGuid()))).Content.ReadFromJsonAsync<MiniAppDto>())!;
        var root = "/api/developer/mini-apps/" + app.AppId;
        var revision = await host.WithServices(async s => await s.GetRequiredService<AppDbContext>().MiniApps.Select(x => x.Version).SingleAsync());
        var first = (await (await http.PostAsJsonAsync(root + "/versions", new MiniAppVersionCreateRequest(Guid.NewGuid(), revision, "1.0"))).Content.ReadFromJsonAsync<MiniAppVersionSummaryDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await http.PatchAsJsonAsync(root, new { name = "First edit" })).StatusCode);
        Assert.Equal("First edit", (await http.GetFromJsonAsync<MiniAppVersionDetailDto>(root + "/versions/" + first.Id))!.Snapshot.Name);
        await host.WithServices(async s => { await s.GetRequiredService<AppDbContext>().MiniAppVersions.Where(x => x.Id == first.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.State, MiniAppVersionState.Submitted).SetProperty(x => x.SubmittedAt, DateTime.UtcNow)); return true; });
        Assert.Equal(HttpStatusCode.OK, (await http.PatchAsJsonAsync(root, new { name = "Next working name" })).StatusCode);
        var history = (await http.GetFromJsonAsync<MiniAppVersionPageDto>(root + "/versions"))!;
        Assert.Equal(2, history.Total); var working = Assert.Single(history.Items, x => x.State == "Draft"); Assert.Equal(2, working.VersionCode);
        Assert.Equal("First edit", (await http.GetFromJsonAsync<MiniAppVersionDetailDto>(root + "/versions/" + first.Id))!.Snapshot.Name);
        var integration = new MiniAppIntegrationRequest("https://partner.example.com/app", "support@example.com", "Short", "Details",
            "https://partner.example.com/privacy", "https://partner.example.com/terms");
        Assert.Equal(HttpStatusCode.OK, (await http.PutAsJsonAsync(root + "/integration", integration)).StatusCode);
        var detail = (await http.GetFromJsonAsync<MiniAppVersionDetailDto>(root + "/versions/" + working.Id))!;
        Assert.Equal("Next working name", detail.Snapshot.Name); Assert.Equal("Details", detail.Snapshot.Description);
        Assert.False(detail.Snapshot.SsoEnabled); Assert.Null(detail.Snapshot.Sandbox);
        Assert.Equal(2, (await http.GetFromJsonAsync<MiniAppVersionPageDto>(root + "/versions"))!.Total);
        Assert.Null(detail.Snapshot.Production);
    }

    [Fact]
    public async Task InvalidStoredDraftRollsBackLegacyMetadataAndItsAudit()
    {
        await using var host = await MiniAppApiTests.Host.Create(review: true); var http = host.Client;
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", await host.Login("version-rollback@example.test"));
        var app = (await (await http.PostAsJsonAsync("/api/developer/mini-apps", new MiniAppCreateRequest("Before rollback", "utilities", Guid.NewGuid()))).Content.ReadFromJsonAsync<MiniAppDto>())!;
        var root = "/api/developer/mini-apps/" + app.AppId;
        var revision = await host.WithServices(async s => await s.GetRequiredService<AppDbContext>().MiniApps.Select(x => x.Version).SingleAsync());
        await http.PostAsJsonAsync(root + "/versions", new MiniAppVersionCreateRequest(Guid.NewGuid(), revision, "1.0"));
        var before = await host.WithServices(async s => await s.GetRequiredService<AppDbContext>().MiniAppAuditLogs.CountAsync());
        await host.WithServices(async s => { await s.GetRequiredService<AppDbContext>().MiniAppVersions.ExecuteUpdateAsync(set => set.SetProperty(x => x.SnapshotHash, new string('0', 64))); return true; });
        Assert.Equal(HttpStatusCode.Conflict, (await http.PatchAsJsonAsync(root, new { name = "Must roll back" })).StatusCode);
        Assert.Equal("Before rollback", (await http.GetFromJsonAsync<MiniAppDto>(root))!.Name);
        Assert.Equal(before, await host.WithServices(async s => await s.GetRequiredService<AppDbContext>().MiniAppAuditLogs.CountAsync()));
    }
}
