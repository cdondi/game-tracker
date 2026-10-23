using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameTracker.Api.Data;
using GameTracker.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace GameTracker.Tests;

public class GetGameTests(GameTrackerApiFactory factory) : IClassFixture<GameTrackerApiFactory>
{
    private static readonly DateTimeOffset CreatedTime = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedTime = new(2021, 6, 15, 12, 30, 0, TimeSpan.Zero);

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Get_existing_id_returns_200_with_the_game()
    {
        var title = TestData.UniqueTitle("Viewed");
        var id = await InsertGameAsync(title);

        var response = await _client.GetAsync($"/games/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, game.GetProperty("id").GetInt32());
        Assert.Equal(title, game.GetProperty("title").GetString());
        Assert.Equal("Switch", game.GetProperty("platform").GetString());
        Assert.Equal("Completed", game.GetProperty("status").GetString());
        Assert.Equal(4.5m, game.GetProperty("rating").GetDecimal());
        Assert.Equal(CreatedTime, game.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(UpdatedTime, game.GetProperty("updatedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Get_returns_the_same_game_as_create()
    {
        var createResponse = await _client.PostAsJsonAsync("/games",
            new { title = TestData.UniqueTitle("Created"), platform = "PC", status = "Backlog" });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();

        var response = await _client.GetAsync(createResponse.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(created.ToString(), game.ToString());
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Get_unknown_id_returns_404_problem_details(int id)
    {
        var response = await _client.GetAsync($"/games/{id}");

        await AssertNotFoundProblemAsync(response);
    }

    [Fact]
    public async Task Get_deleted_game_returns_404_problem_details()
    {
        var id = await InsertGameAsync(TestData.UniqueTitle("Deleted"));
        (await _client.DeleteAsync($"/games/{id}")).EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/games/{id}");

        await AssertNotFoundProblemAsync(response);
    }

    /// <summary>
    /// Inserts a game directly with fixed timestamps and a rating (which the API does not accept
    /// until Story 7), so the test can check every field round-trips.
    /// </summary>
    private async Task<int> InsertGameAsync(string title)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>();
        var game = new Game
        {
            Title = title,
            Platform = Platform.Switch,
            Status = GameStatus.Completed,
            Rating = 4.5m,
            CreatedAt = CreatedTime,
            UpdatedAt = UpdatedTime,
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game.Id;
    }

    private static async Task AssertNotFoundProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }
}
