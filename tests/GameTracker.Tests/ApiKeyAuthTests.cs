using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace GameTracker.Tests;

/// <summary>Hosts the API with API key auth turned on.</summary>
public class ApiKeyAuthApiFactory : GameTrackerApiFactory
{
    public const string ApiKey = "test-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Auth:Enabled", "true");
        builder.UseSetting("Auth:ApiKey", ApiKey);
    }
}

public class ApiKeyAuthTests(ApiKeyAuthApiFactory factory) : IClassFixture<ApiKeyAuthApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private const string ValidBody = """{"title":"Hades","platform":"PC","status":"Backlog"}""";

    public static TheoryData<string, string, string?> EndpointsAndBadKeys()
    {
        var data = new TheoryData<string, string, string?>();
        (string Method, string Path)[] endpoints =
        [
            ("GET", "/games"),
            ("GET", "/games/1"),
            ("POST", "/games"),
            ("PUT", "/games/1"),
            ("DELETE", "/games/1"),
        ];
        foreach (var (method, path) in endpoints)
        {
            foreach (var key in new[] { null, "", "wrong-key", ApiKeyAuthApiFactory.ApiKey + "x" })
            {
                data.Add(method, path, key);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EndpointsAndBadKeys))]
    public async Task Missing_or_wrong_key_returns_401_problem_details(string method, string path, string? key)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            request.Content = new StringContent(ValidBody, Encoding.UTF8, "application/json");
        }
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", key);
        }

        var response = await _client.SendAsync(request);

        await AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task Missing_key_returns_401_even_for_a_malformed_body()
    {
        var response = await _client.PostAsync("/games",
            new StringContent("{ not json", Encoding.UTF8, "application/json"));

        await AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task Key_sent_twice_returns_401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/games");
        request.Headers.Add("X-Api-Key", [ApiKeyAuthApiFactory.ApiKey, ApiKeyAuthApiFactory.ApiKey]);

        var response = await _client.SendAsync(request);

        await AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task Valid_key_reaches_every_endpoint()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKeyAuthApiFactory.ApiKey);
        var title = TestData.UniqueTitle("Authorized");

        var created = await client.PostAsJsonAsync("/games", new { title, platform = "PC", status = "Backlog" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var fetched = await client.GetAsync($"/games/{id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(title, (await fetched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());

        var listed = await client.GetAsync("/games");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var games = await listed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(games.EnumerateArray(), g => g.GetProperty("id").GetInt32() == id);

        var updated = await client.PutAsJsonAsync($"/games/{id}", new { title, platform = "PC", status = "Playing", rating = 4.5 });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(4.5m, (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rating").GetDecimal());

        var deleted = await client.DeleteAsync($"/games/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Swagger_document_describes_the_api_key_header()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        var scheme = doc.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("header", scheme.GetProperty("in").GetString());
        Assert.Equal("X-Api-Key", scheme.GetProperty("name").GetString());
        Assert.Contains(doc.GetProperty("security").EnumerateArray(), requirement => requirement.TryGetProperty("ApiKey", out _));
    }

    private static async Task AssertUnauthorizedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(401, problem.GetProperty("status").GetInt32());
    }
}

public class ApiKeyAuthDisabledTests(GameTrackerApiFactory factory) : IClassFixture<GameTrackerApiFactory>
{
    [Fact]
    public async Task With_auth_disabled_no_key_is_needed()
    {
        var response = await factory.CreateClient().GetAsync("/games");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public class ApiKeyAuthConfigurationTests
{
    private class EnabledWithoutKeyFactory : GameTrackerApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Auth:Enabled", "true");
            builder.UseSetting("Auth:ApiKey", "");
        }
    }

    [Fact]
    public void Enabled_without_a_key_fails_at_startup()
    {
        using var factory = new EnabledWithoutKeyFactory();

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("user-secrets", ex.Message);
    }
}
