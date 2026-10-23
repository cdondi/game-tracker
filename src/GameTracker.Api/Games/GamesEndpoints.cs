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
        games.MapPut("/{id:int}", UpdateGame);
        games.MapDelete("/{id:int}", DeleteGame);

        return routes;
    }

    /// <summary>
    /// Returns games as a bare array, sorted by title ignoring case, optionally filtered by status and/or
    /// platform (combined with AND). Filter values ignore case; an unknown value is a 400, not an empty list.
    /// The ordering runs in SQL, where the Title column's NOCASE collation provides the case-insensitive comparison.
    /// </summary>
    private static async Task<Results<Ok<List<GameResponse>>, ValidationProblem>> ListGames(
        GameTrackerDbContext db, string? status = null, string? platform = null)
    {
        var errors = new Dictionary<string, string[]>();
        var statusFilter = GameValidator.ParseFilter<GameStatus>(status, "status", errors);
        var platformFilter = GameValidator.ParseFilter<Platform>(platform, "platform", errors);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var query = db.Games.AsNoTracking();
        if (statusFilter is { } s)
        {
            query = query.Where(g => g.Status == s);
        }
        if (platformFilter is { } p)
        {
            query = query.Where(g => g.Platform == p);
        }

        var games = await query
            .OrderBy(g => g.Title)
            .ThenBy(g => g.Id)
            .ToListAsync();

        return TypedResults.Ok(games.Select(GameResponse.From).ToList());
    }

    private static async Task<Results<Created<GameResponse>, ValidationProblem>> CreateGame(GameRequest request, GameTrackerDbContext db)
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

    /// <summary>
    /// Replaces every client-editable field. Rating is not accepted yet (Story 7), so an update clears it.
    /// An unknown id is a 404; PUT never creates a game.
    /// </summary>
    private static async Task<Results<Ok<GameResponse>, ValidationProblem, NotFound>> UpdateGame(int id, GameRequest request, GameTrackerDbContext db)
    {
        if (!GameValidator.TryValidate(request, out var valid, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var game = await db.Games.FindAsync(id);
        if (game is null)
        {
            return TypedResults.NotFound();
        }

        game.Title = valid.Title;
        game.Platform = valid.Platform;
        game.Status = valid.Status;
        game.Rating = null;
        game.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();

        return TypedResults.Ok(GameResponse.From(game));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteGame(int id, GameTrackerDbContext db)
    {
        var deleted = await db.Games.Where(g => g.Id == id).ExecuteDeleteAsync();

        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }
}
