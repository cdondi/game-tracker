using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameTracker.Api.Data;
using GameTracker.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace GameTracker.Tests;

public class UpdateGameTests(GameTrackerApiFactory factory) : IClassFixture<GameTrackerApiFactory>
{
    private static readonly DateTimeOffset OldTime = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Put_with_valid_body_returns_200_with_updated_game()
    {
        var id = await InsertGameAsync();
        var title = TestData.UniqueTitle("Updated");

        var response = await _client.PutAsJsonAsync($"/games/{id}", new { title, platform = "Switch", status = "Completed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, game.GetProperty("id").GetInt32());
        Assert.Equal(title, game.GetProperty("title").GetString());
        Assert.Equal("Switch", game.GetProperty("platform").GetString());
        Assert.Equal("Completed", game.GetProperty("status").GetString());

        var stored = await FindGameAsync(id);
        Assert.Equal(title, stored!.Title);
        Assert.Equal(Platform.Switch, stored.Platform);
        Assert.Equal(GameStatus.Completed, stored.Status);
    }

    [Fact]
    public async Task Put_without_rating_clears_existing_rating()
    {
        var id = await InsertGameAsync(status: GameStatus.Playing, rating: 4.5m);

        var response = await _client.PutAsJsonAsync($"/games/{id}",
            new { title = TestData.UniqueTitle(), platform = "PC", status = "Playing" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, game.GetProperty("rating").ValueKind);
        Assert.Null((await FindGameAsync(id))!.Rating);
    }

    [Fact]
    public async Task Put_changes_updatedAt_but_not_createdAt()
    {
        var id = await InsertGameAsync();
        var before = DateTimeOffset.UtcNow;

        var response = await _client.PutAsJsonAsync($"/games/{id}",
            new { title = TestData.UniqueTitle(), platform = "PC", status = "Playing" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(OldTime, game.GetProperty("createdAt").GetDateTimeOffset());
        Assert.True(game.GetProperty("updatedAt").GetDateTimeOffset() >= before);

        var stored = await FindGameAsync(id);
        Assert.Equal(OldTime, stored!.CreatedAt);
        Assert.True(stored.UpdatedAt >= before);
    }

    [Fact]
    public async Task Put_ignores_client_supplied_id_and_timestamps()
    {
        var id = await InsertGameAsync();
        var otherId = await InsertGameAsync();
        var before = DateTimeOffset.UtcNow;
        var clientTime = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var otherTitle = (await FindGameAsync(otherId))!.Title;

        var response = await _client.PutAsJsonAsync($"/games/{id}", new
        {
            id = otherId,
            title = TestData.UniqueTitle(),
            platform = "PC",
            status = "Backlog",
            createdAt = clientTime,
            updatedAt = clientTime,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, game.GetProperty("id").GetInt32());
        Assert.Equal(OldTime, game.GetProperty("createdAt").GetDateTimeOffset());
        Assert.True(game.GetProperty("updatedAt").GetDateTimeOffset() >= before);
        Assert.Equal(otherTitle, (await FindGameAsync(otherId))!.Title);
    }

    [Fact]
    public async Task Put_stores_title_trimmed()
    {
        var id = await InsertGameAsync();
        var title = TestData.UniqueTitle("Hades");

        var response = await _client.PutAsJsonAsync($"/games/{id}", new { title = $"  {title}\t ", platform = "PC", status = "Backlog" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(title, game.GetProperty("title").GetString());
        Assert.Equal(title, (await FindGameAsync(id))!.Title);
    }

    [Fact]
    public async Task Put_accepts_enum_values_in_any_case_and_returns_canonical_casing()
    {
        var id = await InsertGameAsync();

        var response = await _client.PutAsJsonAsync($"/games/{id}",
            new { title = TestData.UniqueTitle(), platform = "xBoXsErIeS", status = "completed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("XboxSeries", game.GetProperty("platform").GetString());
        Assert.Equal("Completed", game.GetProperty("status").GetString());
    }

    public static TheoryData<string, string> InvalidBodies => new()
    {
        { "title", """{ "platform": "PC", "status": "Backlog" }""" },
        { "title", """{ "title": null, "platform": "PC", "status": "Backlog" }""" },
        { "title", """{ "title": "", "platform": "PC", "status": "Backlog" }""" },
        { "title", """{ "title": "   ", "platform": "PC", "status": "Backlog" }""" },
        { "title", $$"""{ "title": "{{new string('a', Game.TitleMaxLength + 1)}}", "platform": "PC", "status": "Backlog" }""" },
        { "platform", """{ "title": "T", "status": "Backlog" }""" },
        { "platform", """{ "title": "T", "platform": null, "status": "Backlog" }""" },
        { "status", """{ "title": "T", "platform": "PC" }""" },
        { "status", """{ "title": "T", "platform": "PC", "status": null }""" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Put_with_invalid_field_returns_400_and_leaves_game_unchanged(string field, string body)
    {
        var id = await InsertGameAsync();
        var original = await FindGameAsync(id);

        var response = await PutRawAsync(id, body);

        var errors = await AssertValidationProblemAsync(response);
        Assert.True(errors.TryGetProperty(field, out _), $"Expected a '{field}' error, got: {errors}");
        var stored = await FindGameAsync(id);
        Assert.Equal(original!.Title, stored!.Title);
        Assert.Equal(OldTime, stored.UpdatedAt);
    }

    [Theory]
    [InlineData("platform", "Dreamcast")]
    [InlineData("platform", "1")]
    [InlineData("status", "Abandoned")]
    [InlineData("status", "0")]
    public async Task Put_with_unknown_enum_value_returns_400_listing_valid_values(string field, string value)
    {
        var id = await InsertGameAsync();
        var body = new Dictionary<string, string>
        {
            ["title"] = TestData.UniqueTitle(),
            ["platform"] = "PC",
            ["status"] = "Backlog",
            [field] = value,
        };

        var response = await _client.PutAsJsonAsync($"/games/{id}", body);

        var errors = await AssertValidationProblemAsync(response);
        Assert.True(errors.TryGetProperty(field, out var fieldErrors), $"Expected a '{field}' error, got: {errors}");
        var message = string.Join(" ", fieldErrors.EnumerateArray().Select(e => e.GetString()));
        var validValues = field == "platform"
            ? Enum.GetNames<Platform>()
            : Enum.GetNames<GameStatus>();
        Assert.All(validValues, name => Assert.Contains(name, message));
    }

    [Theory]
    [InlineData("""{ "title": "T", "platform": "PC", "status": "Backlog" """)]
    [InlineData("""{ "title": 42, "platform": "PC", "status": "Backlog" }""")]
    [InlineData("")]
    public async Task Put_with_malformed_json_returns_400_problem_details(string body)
    {
        var id = await InsertGameAsync();

        var response = await PutRawAsync(id, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Put_unknown_id_returns_404_and_does_not_create_a_game(int id)
    {
        var title = TestData.UniqueTitle("Never created");

        var response = await _client.PutAsJsonAsync($"/games/{id}", new { title, platform = "PC", status = "Backlog" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, problem.GetProperty("status").GetInt32());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>();
        Assert.DoesNotContain(db.Games, g => g.Title == title);
    }

    /// <summary>
    /// Inserts a game directly with old timestamps (and optionally a rating, which the API
    /// does not accept until Story 7), so tests can tell whether a PUT changed them.
    /// </summary>
    private async Task<int> InsertGameAsync(GameStatus status = GameStatus.Backlog, decimal? rating = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>();
        var game = new Game
        {
            Title = TestData.UniqueTitle("Original"),
            Platform = Platform.PS4,
            Status = status,
            Rating = rating,
            CreatedAt = OldTime,
            UpdatedAt = OldTime,
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

    private Task<HttpResponseMessage> PutRawAsync(int id, string json) =>
        _client.PutAsync($"/games/{id}", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    /// <summary>Asserts a 400 ProblemDetails response and returns its per-field <c>errors</c> object.</summary>
    private static async Task<JsonElement> AssertValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("errors", out var errors), $"Expected per-field errors, got: {problem}");
        return errors;
    }
}
