using System.Diagnostics.CodeAnalysis;
using GameTracker.Api.Models;

namespace GameTracker.Api.Games;

/// <summary>A request whose fields have passed validation and been normalized.</summary>
public record ValidatedGame(string Title, Platform Platform, GameStatus Status);

public static class GameValidator
{
    /// <summary>
    /// Validates a request. On failure, <paramref name="errors"/> holds messages keyed by JSON field name.
    /// </summary>
    public static bool TryValidate(
        CreateGameRequest request,
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

        game = errors.Count == 0 ? new ValidatedGame(title, platform!.Value, status!.Value) : null;
        return game is not null;
    }

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
