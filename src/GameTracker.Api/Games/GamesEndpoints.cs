using GameTracker.Api.Data;
using GameTracker.Api.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
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

    private static async Task<Results<Created<GameResponse>, ValidationProblem, ProblemHttpResult>> CreateGame(GameRequest request, GameTrackerDbContext db)
    {
        if (!GameValidator.TryValidate(request, out var valid, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await IsDuplicateAsync(db, valid, exceptId: null))
        {
            return DuplicateProblem(valid);
        }

        var now = DateTimeOffset.UtcNow;
        var game = new Game
        {
            Title = valid.Title,
            Platform = valid.Platform,
            Status = valid.Status,
            Rating = valid.Rating,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Games.Add(game);
        if (!await TrySaveUniqueAsync(db))
        {
            return DuplicateProblem(valid);
        }

        return TypedResults.Created($"/games/{game.Id}", GameResponse.From(game));
    }

    /// <summary>
    /// Replaces every client-editable field, so an omitted rating becomes null. The exception is moving a
    /// rated game to Backlog: the body must say "rating": null explicitly, so a rating is never cleared by
    /// accident. An unknown id is a 404; PUT never creates a game. Keeping the game's own title and platform
    /// (even with different casing) is not a duplicate.
    /// </summary>
    private static async Task<Results<Ok<GameResponse>, ValidationProblem, NotFound, ProblemHttpResult>> UpdateGame(int id, GameRequest request, GameTrackerDbContext db)
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

        if (game.Rating is not null && valid.Status == GameStatus.Backlog && !request.RatingProvided)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["rating"] = ["This game is rated. To move it to Backlog, set rating to null in the same request."],
            });
        }

        if (await IsDuplicateAsync(db, valid, exceptId: id))
        {
            return DuplicateProblem(valid);
        }

        game.Title = valid.Title;
        game.Platform = valid.Platform;
        game.Status = valid.Status;
        game.Rating = valid.Rating;
        game.UpdatedAt = DateTimeOffset.UtcNow;

        if (!await TrySaveUniqueAsync(db))
        {
            return DuplicateProblem(valid);
        }

        return TypedResults.Ok(GameResponse.From(game));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteGame(int id, GameTrackerDbContext db)
    {
        var deleted = await db.Games.Where(g => g.Id == id).ExecuteDeleteAsync();

        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    /// <summary>
    /// The friendly pre-check. The title comparison runs in SQL, where the NOCASE collation ignores case;
    /// the validator has already trimmed the title.
    /// </summary>
    private static Task<bool> IsDuplicateAsync(GameTrackerDbContext db, ValidatedGame valid, int? exceptId) =>
        db.Games.AnyAsync(g => g.Title == valid.Title && g.Platform == valid.Platform && g.Id != exceptId);

    /// <summary>
    /// Saves, returning false if the unique index on (Title, Platform) rejects the change. This catches
    /// a duplicate that slips past <see cref="IsDuplicateAsync"/>, such as a concurrent request.
    /// </summary>
    private static async Task<bool> TrySaveUniqueAsync(GameTrackerDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteUniqueConstraintError })
        {
            return false;
        }
    }

    /// <summary>SQLITE_CONSTRAINT_UNIQUE.</summary>
    private const int SqliteUniqueConstraintError = 2067;

    private static ProblemHttpResult DuplicateProblem(ValidatedGame valid) =>
        TypedResults.Problem(
            title: "Duplicate game",
            detail: $"A game titled '{valid.Title}' on {valid.Platform} already exists.",
            statusCode: StatusCodes.Status409Conflict);
}
