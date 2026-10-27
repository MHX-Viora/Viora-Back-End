using System.Security.Cryptography;
using System.Text;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppVersionSnapshotTests
{
    [Fact]
    public void CanonicalSnapshotHasDeterministicHashAndSortedDeduplicatedArrays()
    {
        var input = Snapshot();
        var first = MiniAppVersionSnapshotPolicy.Create(input);
        var equivalent = input with { Name = " Example ", Sandbox = input.Sandbox! with
        {
            Scopes = ["profile", "openid", "profile"],
            RedirectUris = ["https://partner.example.com/z", "https://partner.example.com/callback", "https://partner.example.com/z"]
        } };
        var second = MiniAppVersionSnapshotPolicy.Create(equivalent);
        Assert.Equal(first.Json, second.Json); Assert.Equal(first.Hash, second.Hash);
        Assert.Equal(["openid", "profile"], first.Snapshot.Sandbox!.Scopes);
        Assert.Equal("partner.example.com", first.Snapshot.Domain);
        Assert.Equal(first.Json, MiniAppVersionSnapshotPolicy.Read(first.Json, first.Hash).Json);
        var changed = MiniAppVersionSnapshotPolicy.Create(input with { ShortDescription = "Different description" });
        Assert.NotEqual(first.Hash, changed.Hash);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("noncanonical")]
    [InlineData("schema")]
    [InlineData("nested-secret")]
    public void StoredSnapshotsRejectTamperingUnknownFieldsAndNoncanonicalEncoding(string invalid)
    {
        var value = MiniAppVersionSnapshotPolicy.Create(Snapshot());
        var json = value.Json;
        switch (invalid)
        {
            case "hash": AssertInvalid(json, new string('0', 64)); return;
            case "unknown": json = json.Insert(1, "\"clientSecret\":\"must-never-enter-snapshot\","); break;
            case "duplicate": json = json.Insert(1, "\"schemaVersion\":1,"); break;
            case "noncanonical": json = " " + json; break;
            case "schema": json = json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"); break;
            case "nested-secret": json = json.Replace("\"clientType\":\"Public\"", "\"clientType\":\"Public\",\"clientSecret\":\"nope\""); break;
        }
        AssertInvalid(json, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant());
    }

    [Theory]
    [InlineData("http")]
    [InlineData("callback-fragment")]
    [InlineData("callback-wildcard")]
    [InlineData("scope")]
    [InlineData("domain")]
    [InlineData("client-pair")]
    [InlineData("client-whitespace")]
    [InlineData("large")]
    public void SnapshotRejectsInvalidBoundariesAndOversizedEncodedJson(string invalid)
    {
        var input = Snapshot();
        input = invalid switch
        {
            "http" => input with { WebsiteUrl = "http://partner.example.com" },
            "callback-fragment" => input with { Sandbox = input.Sandbox! with { RedirectUris = ["https://partner.example.com/callback#token"] } },
            "callback-wildcard" => input with { Sandbox = input.Sandbox! with { RedirectUris = ["https://partner.example.com/*"] } },
            "scope" => input with { Sandbox = input.Sandbox! with { Scopes = ["openid", "admin"] } },
            "domain" => input with { Domain = "other.example.com" },
            "client-pair" => input with { Sandbox = input.Sandbox! with { ClientId = null } },
            "client-whitespace" => input with { Sandbox = input.Sandbox! with { ClientId = "sandbox\nclient" } },
            // Individually valid fields exceed 64 KiB after Unicode JSON escaping and URL serialization.
            "large" => input with { Description = string.Concat(Enumerable.Repeat("😀", 5000)),
                Sandbox = input.Sandbox! with { RedirectUris = Enumerable.Range(0, 20)
                    .Select(i => "https://partner.example.com/" + new string('a', 400) + i).ToArray() } },
            _ => throw new InvalidOperationException()
        };
        Assert.Throws<DeveloperAuthException>(() => MiniAppVersionSnapshotPolicy.Create(input));
    }

    [Fact]
    public void IncompleteDraftAndSsoDisabledSnapshotRemainRepresentableWithoutFakeClients()
    {
        var snapshot = Snapshot() with { SsoEnabled = false, Sandbox = null, Production = null,
            WebsiteUrl = null, Domain = null, SupportEmail = null, PrivacyPolicyUrl = null, TermsOfServiceUrl = null };
        var value = MiniAppVersionSnapshotPolicy.Create(snapshot);
        Assert.Null(value.Snapshot.Sandbox); Assert.Null(value.Snapshot.Production); Assert.Null(value.Snapshot.Domain);
        Assert.DoesNotContain("secret", value.Json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", value.Json, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertInvalid(string json, string hash)
    {
        var error = Assert.Throws<DeveloperAuthException>(() => MiniAppVersionSnapshotPolicy.Read(json, hash));
        Assert.Equal(409, error.Status); Assert.Equal("VERSION_SNAPSHOT_INVALID", error.Code);
    }
    internal static MiniAppVersionSnapshot Snapshot() => new(1, "Example", "utilities", null, "Description", "Details",
        "https://partner.example.com/app", "partner.example.com", "https://partner.example.com/privacy",
        "https://partner.example.com/terms", "support@example.com", true,
        new(Guid.Parse("dbdd170c-1111-4444-aaaa-d39d92c96468"), "sandbox-client", "Public", ["openid", "profile"],
            ["https://partner.example.com/callback", "https://partner.example.com/z"], true), null);
}
