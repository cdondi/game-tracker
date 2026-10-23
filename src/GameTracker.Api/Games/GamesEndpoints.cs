using GameTracker.Api.Data;
using GameTracker.Api.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace GameTracker.Api.Games;

public static class GamesEndpoints
{
    public static IEndpointRouteBuilder MapGamesEndpoints(this IEndpointRouteBuilder routes)
    {
        var games = routes.MapGroup("/games").WithTags("Games");

        games.MapPost("/", CreateGame);

        return routes;
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
