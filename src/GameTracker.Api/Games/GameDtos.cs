using GameTracker.Api.Models;

namespace GameTracker.Api.Games;

/// <summary>
/// The body for POST and PUT. Enum fields are strings so the API can report unknown values per field, with the valid options.
/// Server-owned fields (id, createdAt, updatedAt) are deliberately absent, so client values are ignored.
/// </summary>
public record GameRequest(string? Title, string? Platform, string? Status);

public record GameResponse(
    int Id,
    string Title,
    Platform Platform,
    GameStatus Status,
    decimal? Rating,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static GameResponse From(Game game) =>
        new(game.Id, game.Title, game.Platform, game.Status, game.Rating, game.CreatedAt, game.UpdatedAt);
}
