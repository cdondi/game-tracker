using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameTracker.Api.Data;
using GameTracker.Api.Models;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GameTracker.Tests;

public class DuplicateGameTests(GameTrackerApiFactory factory) : IClassFixture<GameTrackerApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_with_existing_title_and_platform_returns_409_problem_details()
    {
        var title = TestData.UniqueTitle("Elden Ring");
        await CreateGameAsync(title, "PS5");

        var response = await _client.PostAsJsonAsync("/games", new { title, platform = "PS5", status = "Backlog" });

        await AssertConflictAsync(response);
        Assert.Equal(1, await CountGamesAsync(title));
    }

    [Fact]
    public async Task Post_duplicate_check_ignores_case_and_surrounding_whitespace()
    {
        var title = TestData.UniqueTitle("Elden Ring");
        await CreateGameAsync(title, "PS5");

        var response = await _client.PostAsJsonAsync("/games",
            new { title = $"  {title.ToLowerInvariant()}\t ", platform = "ps5", status = "Backlog" });

        await AssertConflictAsync(response);
        Assert.Equal(1, await CountGamesAsync(title));
    }

    [Fact]
    public async Task Post_same_title_on_different_platform_is_allowed()
    {
        var title = TestData.UniqueTitle("Elden Ring");
        await CreateGameAsync(title, "PS5");

        var response = await _client.PostAsJsonAsync("/games", new { title, platform = "PC", status = "Backlog" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, await CountGamesAsync(title));
    }

    [Fact]
    public async Task Put_changing_title_to_collide_with_another_game_returns_409_and_leaves_game_unchanged()
    {
        var existingTitle = TestData.UniqueTitle("Existing");
        await CreateGameAsync(existingTitle, "Switch");
        var originalTitle = TestData.UniqueTitle("Original");
        var id = await CreateGameAsync(originalTitle, "Switch");

        var response = await _client.PutAsJsonAsync($"/games/{id}",
            new { title = existingTitle.ToUpperInvariant(), platform = "Switch", status = "Playing" });

        await AssertConflictAsync(response);
        var stored = await FindGameAsync(id);
        Assert.Equal(originalTitle, stored!.Title);
        Assert.Equal(GameStatus.Backlog, stored.Status);
    }

    [Fact]
    public async Task Put_changing_platform_to_collide_with_another_game_returns_409()
    {
        var title = TestData.UniqueTitle("Shared");
        await CreateGameAsync(title, "Switch");
        var id = await CreateGameAsync(title, "PC");

        var response = await _client.PutAsJsonAsync($"/games/{id}", new { title, platform = "Switch", status = "Backlog" });

        await AssertConflictAsync(response);
        Assert.Equal(Platform.PC, (await FindGameAsync(id))!.Platform);
    }

    [Fact]
    public async Task Put_keeping_own_title_and_platform_does_not_return_409()
    {
        var title = TestData.UniqueTitle("Hades");
        var id = await CreateGameAsync(title, "PC");

        var response = await _client.PutAsJsonAsync($"/games/{id}", new { title, platform = "PC", status = "Completed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GameStatus.Completed, (await FindGameAsync(id))!.Status);
    }

    [Fact]
    public async Task Put_changing_only_casing_of_own_title_does_not_return_409()
    {
        var title = TestData.UniqueTitle("Hades");
        var id = await CreateGameAsync(title, "PC");
        var recased = title.ToUpperInvariant();

        var response = await _client.PutAsJsonAsync($"/games/{id}", new { title = recased, platform = "pc", status = "Backlog" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(recased, (await FindGameAsync(id))!.Title);
    }

    [Fact]
    public async Task Database_rejects_duplicate_inserted_directly_through_DbContext()
    {
        var title = TestData.UniqueTitle("Direct");
        await CreateGameAsync(title, "PS5");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>();
        var now = DateTimeOffset.UtcNow;
        db.Games.Add(new Game
        {
            Title = title.ToLowerInvariant(),
            Platform = Platform.PS5,
            Status = GameStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Post_rejected_by_unique_index_after_passing_app_check_returns_409()
    {
        var title = TestData.UniqueTitle("Race");
        using var racing = CreateRacingFactory(title);
        var client = racing.CreateClient();

        var response = await client.PostAsJsonAsync("/games", new { title, platform = "PS5", status = "Backlog" });

        await AssertConflictAsync(response);
    }

    [Fact]
    public async Task Put_rejected_by_unique_index_after_passing_app_check_returns_409()
    {
        var originalTitle = TestData.UniqueTitle("Original");
        var id = await CreateGameAsync(originalTitle, "PS5");
        var title = TestData.UniqueTitle("Race");
        using var racing = CreateRacingFactory(title);
        var client = racing.CreateClient();

        var response = await client.PutAsJsonAsync($"/games/{id}", new { title, platform = "PS5", status = "Backlog" });

        await AssertConflictAsync(response);
        Assert.Equal(originalTitle, (await FindGameAsync(id))!.Title);
    }

    /// <summary>
    /// Returns a host whose saves of <paramref name="title"/> are preceded by a direct insert of the
    /// same title and platform, simulating a concurrent request that wins the race after the API's
    /// own duplicate check has passed. Only the unique index can catch it.
    /// </summary>
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateRacingFactory(string title) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<GameTrackerDbContext>(options =>
                options.AddInterceptors(new InsertDuplicateBeforeSaveInterceptor(title)))));

    private sealed class InsertDuplicateBeforeSaveInterceptor(string title) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            var saving = context.ChangeTracker.Entries<Game>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified && e.Entity.Title == title)
                .Select(e => e.Entity)
                .FirstOrDefault();

            if (saving is not null)
            {
                var now = DateTimeOffset.UtcNow;
                await context.Database.ExecuteSqlAsync(
                    $"INSERT INTO Games (Title, Platform, Status, CreatedAt, UpdatedAt) VALUES ({title}, {saving.Platform.ToString()}, {"Backlog"}, {now}, {now})",
                    cancellationToken);
            }

            return result;
        }
    }

    private async Task<int> CreateGameAsync(string title, string platform)
    {
        var response = await _client.PostAsJsonAsync("/games", new { title, platform, status = "Backlog" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<JsonElement>();
        return game.GetProperty("id").GetInt32();
    }

    private async Task<Game?> FindGameAsync(int id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>();
        return await db.Games.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id);
    }

    /// <summary>Counts games whose title matches ignoring case (via the column's NOCASE collation).</summary>
    private async Task<int> CountGamesAsync(string title)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>();
        return await db.Games.CountAsync(g => g.Title == title);
    }

    private static async Task AssertConflictAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(409, problem.GetProperty("status").GetInt32());
    }
}
