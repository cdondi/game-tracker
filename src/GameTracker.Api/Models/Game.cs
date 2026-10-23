namespace GameTracker.Api.Models;

public class Game
{
    public const int TitleMaxLength = 200;
    public const decimal MinRating = 1.0m;
    public const decimal MaxRating = 5.0m;

    public int Id { get; set; }
    public required string Title { get; set; }
    public Platform Platform { get; set; }
    public GameStatus Status { get; set; }
    public decimal? Rating { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
