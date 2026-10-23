using GameTracker.Api.Data;
using GameTracker.Api.Games;
using GameTracker.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameTracker.Tests;

/// <summary>
/// Each test builds its own factory, so it starts from a freshly migrated and seeded database
/// that no other test has written to.
/// </summary>
public class SeedDataTests
{
    private static readonly decimal[] ValidRatings = [1.0m, 1.5m, 2.0m, 2.5m, 3.0m, 3.5m, 4.0m, 4.5m, 5.0m];

    [Fact]
    public async Task Startup_seeds_exactly_the_sample_games_into_an_empty_table()
    {
        using var factory = new GameTrackerApiFactory();

        var games = await GetAllGamesAsync(factory);

        Assert.Equal(5, games.Count);
        Assert.Equal(
            GameSeeder.Games.Select(s => (s.Title, s.Platform, s.Status, s.Rating)).OrderBy(g => g.Title),
            games.Select(g => (g.Title, g.Platform, g.Status, g.Rating)).OrderBy(g => g.Title));
        Assert.All(games, g =>
        {
            Assert.NotEqual(default, g.CreatedAt);
            Assert.Equal(g.CreatedAt, g.UpdatedAt);
        });
    }

    [Fact]
    public void Sample_games_cover_several_platforms_every_status_and_rated_and_unrated()
    {
        Assert.Equal(5, GameSeeder.Games.Count);
        Assert.True(GameSeeder.Games.Select(g => g.Platform).Distinct().Count() >= 3);
        Assert.Equal(Enum.GetValues<GameStatus>().ToHashSet(), GameSeeder.Games.Select(g => g.Status).ToHashSet());
        Assert.Contains(GameSeeder.Games, g => g.Rating is not null);
        Assert.Contains(GameSeeder.Games, g => g.Rating is null);
    }

    [Fact]
    public void Sample_games_obey_every_validation_rule()
    {
        Assert.All(GameSeeder.Games, seed =>
        {
            var request = new CreateGameRequest(seed.Title, seed.Platform.ToString(), seed.Status.ToString());
            Assert.True(GameValidator.TryValidate(request, out var valid, out var errors), string.Join("; ", errors.SelectMany(e => e.Value)));
            Assert.Equal(seed.Title, valid.Title);

            if (seed.Rating is { } rating)
            {
                Assert.Contains(rating, ValidRatings);
                Assert.NotEqual(GameStatus.Backlog, seed.Status);
            }
        });

        var keys = GameSeeder.Games.Select(g => (g.Title.Trim().ToUpperInvariant(), g.Platform)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public async Task Seeding_again_does_not_duplicate_the_sample_games()
    {
        using var factory = new GameTrackerApiFactory();
        var before = await GetAllGamesAsync(factory);

        // Migrate runs the seeder, just as it does on every startup.
        await MigrateAsync(factory);

        var after = await GetAllGamesAsync(factory);
        Assert.Equal(before.Select(g => g.Id).Order(), after.Select(g => g.Id).Order());
    }

    [Fact]
    public async Task Seeding_skips_a_table_that_already_has_rows()
    {
        using var factory = new GameTrackerApiFactory();
        var title = TestData.UniqueTitle("Kept");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>();
            await db.Games.ExecuteDeleteAsync();
            var now = DateTimeOffset.UtcNow;
            db.Games.Add(new Game { Title = title, Platform = Platform.PC, Status = GameStatus.Backlog, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        await MigrateAsync(factory);

        var game = Assert.Single(await GetAllGamesAsync(factory));
        Assert.Equal(title, game.Title);
    }

    private static async Task MigrateAsync(GameTrackerApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>().Database.MigrateAsync();
    }

    private static async Task<List<Game>> GetAllGamesAsync(GameTrackerApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>().Games.AsNoTracking().ToListAsync();
    }
}
