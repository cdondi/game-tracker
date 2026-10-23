using System.Text.Json.Serialization;
using GameTracker.Api.Models;

namespace GameTracker.Api.Games;

/// <summary>
/// The body for POST and PUT. Enum fields are strings so the API can report unknown values per field, with the valid options.
/// Server-owned fields (id, createdAt, updatedAt) are deliberately absent, so client values are ignored.
/// </summary>
public record GameRequest(string? Title, string? Platform, string? Status)
{
    private readonly decimal? _rating;

    /// <summary>Optional. An omitted rating and an explicit <c>null</c> both mean unrated.</summary>
    public decimal? Rating
    {
        get => _rating;
        init
        {
            _rating = value;
            RatingProvided = true;
        }
    }

    /// <summary>
    /// True when the body contained <c>rating</c>, even as <c>null</c>. The serializer only calls the
    /// setter for properties present in the JSON, so this tells an omitted rating from an explicit null.
    /// </summary>
    [JsonIgnore]
    public bool RatingProvided { get; private init; }
}

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
