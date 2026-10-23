using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GameTracker.Tests;

public class ListGamesTests(GameTrackerApiFactory factory) : IClassFixture<GameTrackerApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Get_returns_200_with_a_bare_json_array_containing_created_games()
    {
        var created = await CreateGameAsync(TestData.UniqueTitle("Celeste"), "Switch", "Completed");

        var response = await _client.GetAsync("/games");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);

        var game = Assert.Single(body.EnumerateArray(), g => g.GetProperty("id").GetInt32() == created.GetProperty("id").GetInt32());
        Assert.Equal(created.GetProperty("title").GetString(), game.GetProperty("title").GetString());
        Assert.Equal("Switch", game.GetProperty("platform").GetString());
        Assert.Equal("Completed", game.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, game.GetProperty("rating").ValueKind);
        Assert.True(game.GetProperty("createdAt").TryGetDateTimeOffset(out _));
        Assert.True(game.GetProperty("updatedAt").TryGetDateTimeOffset(out _));
    }

    [Fact]
    public async Task Get_sorts_by_title_ignoring_case()
    {
        // A shared prefix keeps these together; the suffixes sort differently by ordinal ("Banana" < "apple")
        // than when ignoring case ("apple" < "Banana" < "cherry").
        var prefix = TestData.UniqueTitle("Sort");
        var cherry = await CreateGameAsync($"{prefix} cherry", "PC", "Backlog");
        var banana = await CreateGameAsync($"{prefix} Banana", "PC", "Backlog");
        var apple = await CreateGameAsync($"{prefix} apple", "PC", "Backlog");

        var ids = await GetListedIdsAsync();

        var ours = ids.Where(new[] { Id(apple), Id(banana), Id(cherry) }.Contains).ToList();
        Assert.Equal([Id(apple), Id(banana), Id(cherry)], ours);
    }

    [Fact]
    public async Task Get_returns_every_game_without_pagination()
    {
        var created = new List<int>();
        for (var i = 0; i < 120; i++)
        {
            created.Add(Id(await CreateGameAsync(TestData.UniqueTitle("Bulk"), "Mobile", "Backlog")));
        }

        var ids = await GetListedIdsAsync();

        Assert.All(created, id => Assert.Contains(id, ids));
    }

    private static int Id(JsonElement game) => game.GetProperty("id").GetInt32();

    private async Task<List<int>> GetListedIdsAsync()
    {
        var response = await _client.GetAsync("/games");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().Select(Id).ToList();
    }

    private async Task<JsonElement> CreateGameAsync(string title, string platform, string status)
    {
        var response = await _client.PostAsJsonAsync("/games", new { title, platform, status });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
