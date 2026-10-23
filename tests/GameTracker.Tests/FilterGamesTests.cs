using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GameTracker.Tests;

public class FilterGamesTests(GameTrackerApiFactory factory) : IClassFixture<GameTrackerApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Status_filter_returns_only_games_with_that_status()
    {
        var backlog = await CreateGameAsync(TestData.UniqueTitle("Hades"), "PC", "Backlog");
        var playing = await CreateGameAsync(TestData.UniqueTitle("Hades"), "PC", "Playing");

        var games = await GetGamesAsync("/games?status=Backlog");

        Assert.All(games, g => Assert.Equal("Backlog", g.GetProperty("status").GetString()));
        Assert.Contains(Id(backlog), games.Select(Id));
        Assert.DoesNotContain(Id(playing), games.Select(Id));
    }

    [Fact]
    public async Task Platform_filter_returns_only_games_on_that_platform()
    {
        var ps5 = await CreateGameAsync(TestData.UniqueTitle("Returnal"), "PS5", "Backlog");
        var pc = await CreateGameAsync(TestData.UniqueTitle("Returnal"), "PC", "Backlog");

        var games = await GetGamesAsync("/games?platform=PS5");

        Assert.All(games, g => Assert.Equal("PS5", g.GetProperty("platform").GetString()));
        Assert.Contains(Id(ps5), games.Select(Id));
        Assert.DoesNotContain(Id(pc), games.Select(Id));
    }

    [Fact]
    public async Task Both_filters_are_combined_with_and()
    {
        var title = TestData.UniqueTitle("Zelda");
        var switchBacklog = await CreateGameAsync(title, "Switch", "Backlog");
        var switchPlaying = await CreateGameAsync(TestData.UniqueTitle("Zelda"), "Switch", "Playing");
        var pcBacklog = await CreateGameAsync(title, "PC", "Backlog");

        var games = await GetGamesAsync("/games?status=Backlog&platform=Switch");

        Assert.All(games, g =>
        {
            Assert.Equal("Backlog", g.GetProperty("status").GetString());
            Assert.Equal("Switch", g.GetProperty("platform").GetString());
        });
        var ids = games.Select(Id).ToList();
        Assert.Contains(Id(switchBacklog), ids);
        Assert.DoesNotContain(Id(switchPlaying), ids);
        Assert.DoesNotContain(Id(pcBacklog), ids);
    }

    [Theory]
    [InlineData("/games?status=backlog&platform=switch")]
    [InlineData("/games?status=BACKLOG&platform=SWITCH")]
    [InlineData("/games?status=bAcKlOg&platform=sWiTcH")]
    public async Task Filter_values_ignore_case(string url)
    {
        var game = await CreateGameAsync(TestData.UniqueTitle("Metroid"), "Switch", "Backlog");

        var games = await GetGamesAsync(url);

        Assert.Contains(Id(game), games.Select(Id));
        Assert.All(games, g =>
        {
            Assert.Equal("Backlog", g.GetProperty("status").GetString());
            Assert.Equal("Switch", g.GetProperty("platform").GetString());
        });
    }

    [Theory]
    [InlineData("/games?status=Abandoned", "status")]
    [InlineData("/games?status=1", "status")]
    [InlineData("/games?platform=Dreamcast", "platform")]
    [InlineData("/games?status=Backlog&platform=Dreamcast", "platform")]
    public async Task Invalid_filter_value_returns_400_problem_details(string url, string field)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var messages = problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.NotEmpty(messages);
        Assert.Contains("Valid values", messages[0]);
    }

    [Fact]
    public async Task Invalid_status_and_platform_are_both_reported()
    {
        var response = await _client.GetAsync("/games?status=Abandoned&platform=Dreamcast");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("status", out _));
        Assert.True(errors.TryGetProperty("platform", out _));
    }

    [Fact]
    public async Task Filtered_results_keep_title_sort_ignoring_case()
    {
        var prefix = TestData.UniqueTitle("FilterSort");
        var cherry = await CreateGameAsync($"{prefix} cherry", "XboxSeries", "Playing");
        var banana = await CreateGameAsync($"{prefix} Banana", "XboxSeries", "Playing");
        var apple = await CreateGameAsync($"{prefix} apple", "XboxSeries", "Playing");

        var games = await GetGamesAsync("/games?status=Playing&platform=XboxSeries");

        var expected = new[] { Id(apple), Id(banana), Id(cherry) };
        var ours = games.Select(Id).Where(expected.Contains).ToList();
        Assert.Equal(expected, ours);
    }

    private static int Id(JsonElement game) => game.GetProperty("id").GetInt32();

    private async Task<List<JsonElement>> GetGamesAsync(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        return body.EnumerateArray().ToList();
    }

    private async Task<JsonElement> CreateGameAsync(string title, string platform, string status)
    {
        var response = await _client.PostAsJsonAsync("/games", new { title, platform, status });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
