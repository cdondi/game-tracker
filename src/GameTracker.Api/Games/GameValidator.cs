using System.Diagnostics.CodeAnalysis;
using GameTracker.Api.Models;

namespace GameTracker.Api.Games;

/// <summary>A request whose fields have passed validation and been normalized.</summary>
public record ValidatedGame(string Title, Platform Platform, GameStatus Status, decimal? Rating);

public static class GameValidator
{
    /// <summary>
    /// Validates a request. On failure, <paramref name="errors"/> holds messages keyed by JSON field name.
    /// </summary>
    public static bool TryValidate(
        GameRequest request,
        [NotNullWhen(true)] out ValidatedGame? game,
        out Dictionary<string, string[]> errors)
    {
        errors = [];

        var title = request.Title?.Trim() ?? "";
        if (title.Length == 0)
        {
            errors["title"] = ["Title is required."];
        }
        else if (title.Length > Game.TitleMaxLength)
        {
            errors["title"] = [$"Title must be at most {Game.TitleMaxLength} characters."];
        }

        var platform = ParseEnum<Platform>(request.Platform, "platform", errors);
        var status = ParseEnum<GameStatus>(request.Status, "status", errors);

        if (request.Rating is { } rating)
        {
            if (rating < Game.MinRating || rating > Game.MaxRating || rating * 2 % 1 != 0)
            {
                errors["rating"] = [$"Rating must be between {Game.MinRating:0.0} and {Game.MaxRating:0.0} in steps of 0.5."];
            }
            else if (status == GameStatus.Backlog)
            {
                errors["rating"] = ["A Backlog game can't have a rating. Set rating to null, or use status Playing or Completed."];
            }
        }

        game = errors.Count == 0 ? new ValidatedGame(title, platform!.Value, status!.Value, request.Rating) : null;
        return game is not null;
    }

    /// <summary>
    /// Parses an optional filter value. A null value means "no filter" and is valid;
    /// any other value must name an enum member.
    /// </summary>
    public static TEnum? ParseFilter<TEnum>(string? value, string field, Dictionary<string, string[]> errors)
        where TEnum : struct, Enum
        => value is null ? null : ParseEnum<TEnum>(value, field, errors);

    /// <summary>
    /// Matches an enum by name, ignoring case. Unlike <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>,
    /// numeric strings such as "1" are rejected.
    /// </summary>
    private static TEnum? ParseEnum<TEnum>(string? value, string field, Dictionary<string, string[]> errors)
        where TEnum : struct, Enum
    {
        if (value is null)
        {
            errors[field] = [$"{Capitalize(field)} is required."];
            return null;
        }

        var name = Enum.GetNames<TEnum>().FirstOrDefault(n => n.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            errors[field] = [$"'{value}' is not a valid {field}. Valid values: {string.Join(", ", Enum.GetNames<TEnum>())}."];
            return null;
        }

        return Enum.Parse<TEnum>(name);
    }

    private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}
