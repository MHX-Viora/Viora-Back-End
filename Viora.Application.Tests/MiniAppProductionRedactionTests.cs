using System.Text.Json;
using Viora.Application.Developers;
using Xunit;
public sealed class MiniAppProductionRedactionTests
{
    [Fact]
    public void PrivateWireRecordsDoNotExposeCredentialsThroughRecordFormatting()
    {
        var secret = new string('s', 43);
        var receipt = new MiniAppLaunchReceiptDto(Guid.NewGuid(), Guid.NewGuid(), "1.0", "https://host.example.com",
            "https://host.example.com/mini-app-host", DateTime.UtcNow.AddMinutes(1), secret, ["platform.getContext"], Guid.NewGuid());
        object[] values = [new MiniAppHostPrepareDto(secret, secret, DateTime.UtcNow),
            new MiniAppLaunchRequest(Guid.NewGuid(), "Web", secret, secret), new MiniAppRedeemRequest(secret, secret), receipt];
        foreach (var value in values) Assert.False(value.ToString()!.Contains(secret), value.GetType().Name + " exposes a credential in formatted output.");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(secret, json.RootElement.GetProperty("grant").GetString());
    }
}
