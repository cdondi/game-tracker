using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameTracker.Api.Data;
using GameTracker.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace GameTracker.Tests;

public class CreateGameTests(GameTrackerApiFactory factory) : IClassFixture<GameTrackerApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_with_valid_body_returns_201_with_created_game_and_location()
    {
        var title = TestData.UniqueTitle("Elden Ring");

        var response = await _client.PostAsJsonAsync("/games", new { title, platform = "PS5", status = "Playing" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = game.GetProperty("id").GetInt32();
        Assert.True(id > 0);
        Assert.Equal($"/games/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal(title, game.GetProperty("title").GetString());
        Assert.Equal("PS5", game.GetProperty("platform").GetString());
        Assert.Equal("Playing", game.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, game.GetProperty("rating").ValueKind);
        Assert.True(game.GetProperty("createdAt").TryGetDateTimeOffset(out _));
        Assert.True(game.GetProperty("updatedAt").TryGetDateTimeOffset(out _));
    }

    [Fact]
    public async Task Post_stores_title_trimmed()
    {
        var title = TestData.UniqueTitle("Hades");

        var response = await _client.PostAsJsonAsync("/games", new { title = $"  {title}\t ", platform = "PC", status = "Backlog" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(title, game.GetProperty("title").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>();
        var stored = await db.Games.FindAsync(game.GetProperty("id").GetInt32());
        Assert.Equal(title, stored!.Title);
    }

    public static TheoryData<string> InvalidTitleBodies => new()
    {
        """{ "platform": "PC", "status": "Backlog" }""",
        """{ "title": null, "platform": "PC", "status": "Backlog" }""",
        """{ "title": "", "platform": "PC", "status": "Backlog" }""",
        """{ "title": "   ", "platform": "PC", "status": "Backlog" }""",
        $$"""{ "title": "{{new string('a', Game.TitleMaxLength + 1)}}", "platform": "PC", "status": "Backlog" }""",
    };

    [Theory]
    [MemberData(nameof(InvalidTitleBodies))]
    public async Task Post_with_invalid_title_returns_400_with_title_error(string body)
    {
        var response = await PostRawAsync(body);

        var errors = await AssertValidationProblemAsync(response);
        Assert.True(errors.TryGetProperty("title", out _), $"Expected a 'title' error, got: {errors}");
    }

    [Fact]
    public async Task Post_accepts_title_of_exactly_200_characters_after_trimming()
    {
        var prefix = TestData.UniqueTitle();
        var title = prefix + new string('x', Game.TitleMaxLength - prefix.Length);

        var response = await _client.PostAsJsonAsync("/games", new { title = $"  {title}  ", platform = "PC", status = "Backlog" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("platform", """{ "title": "T", "status": "Backlog" }""")]
    [InlineData("platform", """{ "title": "T", "platform": null, "status": "Backlog" }""")]
    [InlineData("status", """{ "title": "T", "platform": "PC" }""")]
    [InlineData("status", """{ "title": "T", "platform": "PC", "status": null }""")]
    public async Task Post_with_missing_enum_field_returns_400_for_that_field(string field, string body)
    {
        var response = await PostRawAsync(body);

        var errors = await AssertValidationProblemAsync(response);
        Assert.True(errors.TryGetProperty(field, out _), $"Expected a '{field}' error, got: {errors}");
    }

    [Theory]
    [InlineData("platform", "Dreamcast")]
    [InlineData("platform", "1")]
    [InlineData("platform", "")]
    [InlineData("status", "Abandoned")]
    [InlineData("status", "0")]
    public async Task Post_with_unknown_enum_value_returns_400_listing_valid_values(string field, string value)
    {
        var body = new Dictionary<string, string>
        {
            ["title"] = TestData.UniqueTitle(),
            ["platform"] = "PC",
            ["status"] = "Backlog",
            [field] = value,
        };

        var response = await _client.PostAsJsonAsync("/games", body);

        var errors = await AssertValidationProblemAsync(response);
        Assert.True(errors.TryGetProperty(field, out var fieldErrors), $"Expected a '{field}' error, got: {errors}");
        var message = string.Join(" ", fieldErrors.EnumerateArray().Select(e => e.GetString()));
        var validValues = field == "platform"
            ? Enum.GetNames<Platform>()
            : Enum.GetNames<GameStatus>();
        Assert.All(validValues, name => Assert.Contains(name, message));
    }

    [Fact]
    public async Task Post_accepts_enum_values_in_any_case_and_returns_canonical_casing()
    {
        var response = await _client.PostAsJsonAsync("/games",
            new { title = TestData.UniqueTitle(), platform = "xBoXsErIeS", status = "completed" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("XboxSeries", game.GetProperty("platform").GetString());
        Assert.Equal("Completed", game.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Post_ignores_client_supplied_id_and_timestamps()
    {
        var before = DateTimeOffset.UtcNow;
        var clientTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var response = await _client.PostAsJsonAsync("/games", new
        {
            id = 999_999,
            title = TestData.UniqueTitle(),
            platform = "Switch",
            status = "Backlog",
            createdAt = clientTime,
            updatedAt = clientTime,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(999_999, game.GetProperty("id").GetInt32());
        Assert.True(game.GetProperty("createdAt").GetDateTimeOffset() >= before);
        Assert.True(game.GetProperty("updatedAt").GetDateTimeOffset() >= before);
    }

    [Fact]
    public async Task Post_with_several_invalid_fields_reports_each_field()
    {
        var response = await PostRawAsync("""{ "title": " ", "platform": "Dreamcast" }""");

        var errors = await AssertValidationProblemAsync(response);
        Assert.True(errors.TryGetProperty("title", out _));
        Assert.True(errors.TryGetProperty("platform", out _));
        Assert.True(errors.TryGetProperty("status", out _));
    }

    [Theory]
    [InlineData("""{ "title": "T", "platform": "PC", "status": "Backlog" """)]
    [InlineData("""{ "title": 42, "platform": "PC", "status": "Backlog" }""")]
    [InlineData("""{ "title": "T", "platform": 1, "status": "Backlog" }""")]
    [InlineData("")]
    public async Task Post_with_malformed_json_returns_400_problem_details(string body)
    {
        var response = await PostRawAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private Task<HttpResponseMessage> PostRawAsync(string json) =>
        _client.PostAsync("/games", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

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
