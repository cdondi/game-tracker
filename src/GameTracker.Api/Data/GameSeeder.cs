using GameTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace GameTracker.Api.Data;

public record SeedGame(string Title, Platform Platform, GameStatus Status, decimal? Rating);

/// <summary>
/// Sample games for the demo. EF Core runs the seeder on every <c>Database.Migrate()</c>,
/// so it only inserts when the Games table is empty; restarts never create duplicates.
/// </summary>
public static class GameSeeder
{
    public static IReadOnlyList<SeedGame> Games { get; } =
    [
        new("The Legend of Zelda: Tears of the Kingdom", Platform.Switch, GameStatus.Completed, 5.0m),
        new("Elden Ring", Platform.PS5, GameStatus.Completed, 4.5m),
        new("Baldur's Gate 3", Platform.PC, GameStatus.Playing, 4.0m),
        new("Hades II", Platform.PC, GameStatus.Playing, null),
        new("Starfield", Platform.XboxSeries, GameStatus.Backlog, null),
    ];

    public static void Seed(DbContext context, bool _)
    {
        var games = context.Set<Game>();
        if (games.Any())
        {
            return;
        }

        games.AddRange(CreateGames());
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, bool _, CancellationToken cancellationToken)
    {
        var games = context.Set<Game>();
        if (await games.AnyAsync(cancellationToken))
        {
            return;
        }

        games.AddRange(CreateGames());
        await context.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<Game> CreateGames()
    {
        var now = DateTimeOffset.UtcNow;
        return Games.Select(seed => new Game
        {
            Title = seed.Title,
            Platform = seed.Platform,
            Status = seed.Status,
            Rating = seed.Rating,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }
}
