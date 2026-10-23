using GameTracker.Api.Data;
using GameTracker.Api.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace GameTracker.Api.Games;

public static class GamesEndpoints
{
    public static IEndpointRouteBuilder MapGamesEndpoints(this IEndpointRouteBuilder routes)
    {
        var games = routes.MapGroup("/games").WithTags("Games");

        games.MapGet("/", ListGames);
        games.MapPost("/", CreateGame);

        return routes;
    }

    /// <summary>
    /// Returns every game as a bare array, sorted by title ignoring case. The ordering runs in SQL,
    /// where the Title column's NOCASE collation provides the case-insensitive comparison.
    /// </summary>
    private static async Task<Ok<List<GameResponse>>> ListGames(GameTrackerDbContext db)
    {
        var games = await db.Games
            .AsNoTracking()
            .OrderBy(g => g.Title)
            .ThenBy(g => g.Id)
            .ToListAsync();

        return TypedResults.Ok(games.Select(GameResponse.From).ToList());
    }

    private static async Task<Results<Created<GameResponse>, ValidationProblem>> CreateGame(CreateGameRequest request, GameTrackerDbContext db)
    {
        if (!GameValidator.TryValidate(request, out var valid, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var now = DateTimeOffset.UtcNow;
        var game = new Game
        {
            Title = valid.Title,
            Platform = valid.Platform,
            Status = valid.Status,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Games.Add(game);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/games/{game.Id}", GameResponse.From(game));
    }
}
