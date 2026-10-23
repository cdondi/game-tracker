using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameTracker.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace GameTracker.Tests;

public class DeleteGameTests(GameTrackerApiFactory factory) : IClassFixture<GameTrackerApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Delete_existing_game_returns_204_and_removes_it()
    {
        var id = await CreateGameAsync();

        var response = await _client.DeleteAsync($"/games/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());

        // Checked against the database rather than GET /games/{id}, which is Story 2.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>();
        Assert.Null(await db.Games.FindAsync(id));
    }

    [Fact]
    public async Task Delete_leaves_other_games_in_place()
    {
        var deletedId = await CreateGameAsync();
        var keptId = await CreateGameAsync();

        var response = await _client.DeleteAsync($"/games/{deletedId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>();
        Assert.NotNull(await db.Games.FindAsync(keptId));
    }

    [Fact]
    public async Task Delete_same_game_twice_returns_404_the_second_time()
    {
        var id = await CreateGameAsync();
        await _client.DeleteAsync($"/games/{id}");

        var response = await _client.DeleteAsync($"/games/{id}");

        await AssertNotFoundProblemAsync(response);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Delete_unknown_id_returns_404_problem_details(int id)
    {
        var response = await _client.DeleteAsync($"/games/{id}");

        await AssertNotFoundProblemAsync(response);
    }

    private async Task<int> CreateGameAsync()
    {
        var response = await _client.PostAsJsonAsync("/games",
            new { title = TestData.UniqueTitle("Delete me"), platform = "PC", status = "Backlog" });
        response.EnsureSuccessStatusCode();
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        return game.GetProperty("id").GetInt32();
    }

    private static async Task AssertNotFoundProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }
}
