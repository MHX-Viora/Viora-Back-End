using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Application.Tests.Live;

public sealed class LiveGiftCinematicDurationTests
{
    [Theory]
    [InlineData(3000, true)]
    [InlineData(4700, true)]
    [InlineData(5600, true)]
    [InlineData(6000, true)]
    [InlineData(7000, true)]
    [InlineData(7001, false)]
    public async Task AdminGiftAcceptsLegacyAndCinematicDurationsUpToSevenSeconds(int durationMs, bool accepted)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("char_length", (string value) => value.Length);
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection).Options);
        var controller = new LiveGiftsController(db);
        var body = new LiveGiftBody("Vương miện", "https://example.com/crown.png", null,
            100, LiveGiftAnimationType.Fullscreen, 1, true, LiveGiftEffectType.Crown, 3, durationMs);

        // Invalid requests return before EF needs a database; valid requests need a live connection.
        if (accepted)
        {
            await db.Database.EnsureCreatedAsync();
        }

        var result = await controller.Create(body, CancellationToken.None);
        if (accepted) Assert.IsType<CreatedResult>(result);
        else Assert.IsType<UnprocessableEntityObjectResult>(result);
    }
}
