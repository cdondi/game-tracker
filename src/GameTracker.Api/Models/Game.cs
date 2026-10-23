namespace GameTracker.Api.Models;

public class Game
{
    public const int TitleMaxLength = 200;

    public int Id { get; set; }
    public required string Title { get; set; }
    public Platform Platform { get; set; }
    public GameStatus Status { get; set; }
    public decimal? Rating { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
