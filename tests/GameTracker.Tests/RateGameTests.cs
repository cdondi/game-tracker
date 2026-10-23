using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameTracker.Api.Data;
using GameTracker.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace GameTracker.Tests;

public class RateGameTests(GameTrackerApiFactory factory) : IClassFixture<GameTrackerApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    public static TheoryData<string, decimal> ValidRatings()
    {
        var data = new TheoryData<string, decimal>();
        foreach (var status in new[] { "Playing", "Completed" })
        {
            foreach (var rating in new[] { 1.0m, 1.5m, 2.0m, 2.5m, 3.0m, 3.5m, 4.0m, 4.5m, 5.0m })
            {
                data.Add(status, rating);
            }
        }
        return data;
    }

    public static TheoryData<string> InvalidRatings => new() { "0.5", "0", "-1", "-0.5", "5.5", "6", "3.2", "4.75", "1.01" };

    [Theory]
    [MemberData(nameof(ValidRatings))]
    public async Task Post_accepts_valid_rating_when_playing_or_completed(string status, decimal rating)
    {
        var response = await _client.PostAsJsonAsync("/games", new { title = TestData.UniqueTitle(), platform = "PC", status, rating });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(rating, game.GetProperty("rating").GetDecimal());
        Assert.Equal(rating, (await FindGameAsync(game.GetProperty("id").GetInt32()))!.Rating);
    }

    [Theory]
    [MemberData(nameof(InvalidRatings))]
    public async Task Post_with_out_of_range_or_off_step_rating_returns_400(string rating)
    {
        var response = await PostRawAsync($$"""{ "title": "{{TestData.UniqueTitle()}}", "platform": "PC", "status": "Completed", "rating": {{rating}} }""");

        await AssertRatingErrorAsync(response);
    }

    [Fact]
    public async Task Post_with_rating_and_backlog_status_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/games", new { title = TestData.UniqueTitle(), platform = "PC", status = "Backlog", rating = 3.0m });

        await AssertRatingErrorAsync(response);
    }

    [Theory]
    [InlineData("Backlog")]
    [InlineData("Playing")]
    [InlineData("Completed")]
    public async Task Post_with_null_rating_is_valid_for_every_status(string status)
    {
        var response = await PostRawAsync($$"""{ "title": "{{TestData.UniqueTitle()}}", "platform": "PC", "status": "{{status}}", "rating": null }""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, game.GetProperty("rating").ValueKind);
    }

    [Theory]
    [MemberData(nameof(ValidRatings))]
    public async Task Put_accepts_valid_rating_when_playing_or_completed(string status, decimal rating)
    {
        var id = await InsertGameAsync(GameStatus.Backlog, null);

        var response = await _client.PutAsJsonAsync($"/games/{id}", new { title = TestData.UniqueTitle(), platform = "PC", status, rating });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(rating, game.GetProperty("rating").GetDecimal());
        Assert.Equal(rating, (await FindGameAsync(id))!.Rating);
    }

    [Theory]
    [MemberData(nameof(InvalidRatings))]
    public async Task Put_with_out_of_range_or_off_step_rating_returns_400_and_leaves_game_unchanged(string rating)
    {
        var id = await InsertGameAsync(GameStatus.Playing, 2.0m);

        var response = await PutRawAsync(id, $$"""{ "title": "{{TestData.UniqueTitle()}}", "platform": "PC", "status": "Completed", "rating": {{rating}} }""");

        await AssertRatingErrorAsync(response);
        var stored = await FindGameAsync(id);
        Assert.Equal(GameStatus.Playing, stored!.Status);
        Assert.Equal(2.0m, stored.Rating);
    }

    [Fact]
    public async Task Put_with_rating_and_backlog_status_returns_400()
    {
        var id = await InsertGameAsync(GameStatus.Playing, null);

        var response = await _client.PutAsJsonAsync($"/games/{id}", new { title = TestData.UniqueTitle(), platform = "PC", status = "Backlog", rating = 3.0m });

        await AssertRatingErrorAsync(response);
        Assert.Equal(GameStatus.Playing, (await FindGameAsync(id))!.Status);
    }

    [Fact]
    public async Task Put_moving_rated_game_to_backlog_with_explicit_null_rating_succeeds()
    {
        var id = await InsertGameAsync(GameStatus.Completed, 4.5m);

        var response = await PutRawAsync(id, $$"""{ "title": "{{TestData.UniqueTitle()}}", "platform": "PC", "status": "Backlog", "rating": null }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Backlog", game.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, game.GetProperty("rating").ValueKind);
        var stored = await FindGameAsync(id);
        Assert.Equal(GameStatus.Backlog, stored!.Status);
        Assert.Null(stored.Rating);
    }

    [Fact]
    public async Task Put_moving_rated_game_to_backlog_without_rating_returns_400_and_keeps_rating()
    {
        var id = await InsertGameAsync(GameStatus.Completed, 4.5m);

        var response = await PutRawAsync(id, $$"""{ "title": "{{TestData.UniqueTitle()}}", "platform": "PC", "status": "Backlog" }""");

        await AssertRatingErrorAsync(response);
        var stored = await FindGameAsync(id);
        Assert.Equal(GameStatus.Completed, stored!.Status);
        Assert.Equal(4.5m, stored.Rating);
    }

    [Fact]
    public async Task Put_moving_unrated_game_to_backlog_without_rating_succeeds()
    {
        var id = await InsertGameAsync(GameStatus.Playing, null);

        var response = await PutRawAsync(id, $$"""{ "title": "{{TestData.UniqueTitle()}}", "platform": "PC", "status": "Backlog" }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Playing")]
    [InlineData("Completed")]
    public async Task Put_with_null_rating_clears_rating(string status)
    {
        var id = await InsertGameAsync(GameStatus.Completed, 3.5m);

        var response = await PutRawAsync(id, $$"""{ "title": "{{TestData.UniqueTitle()}}", "platform": "PC", "status": "{{status}}", "rating": null }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await FindGameAsync(id))!.Rating);
    }

    private async Task<int> InsertGameAsync(GameStatus status, decimal? rating)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var game = new Game
        {
            Title = TestData.UniqueTitle("Rated"),
            Platform = Platform.PS5,
            Status = status,
            Rating = rating,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game.Id;
    }

    private async Task<Game?> FindGameAsync(int id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>();
        return await db.Games.FindAsync(id);
    }

    private Task<HttpResponseMessage> PostRawAsync(string json) =>
        _client.PostAsync("/games", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    private Task<HttpResponseMessage> PutRawAsync(int id, string json) =>
        _client.PutAsync($"/games/{id}", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    /// <summary>Asserts a 400 ProblemDetails response with an error for the <c>rating</c> field.</summary>
    private static async Task AssertRatingErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("errors", out var errors), $"Expected per-field errors, got: {problem}");
        Assert.True(errors.TryGetProperty("rating", out _), $"Expected a 'rating' error, got: {errors}");
    }
}
