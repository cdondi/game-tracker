using GameTracker.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameTracker.Tests;

/// <summary>
/// Hosts the API against an in-memory SQLite database. The connection stays open
/// for the factory's lifetime, because an in-memory database disappears when its
/// last connection closes. API key auth is off, so these tests cover the v1 no-auth behavior.
/// </summary>
public class GameTrackerApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public GameTrackerApiFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:Enabled", "false");
        builder.ConfigureTestServices(services =>
        {
            // EF Core 9 registers both of these for AddDbContext; remove both so the
            // file-based SQLite configuration from Program.cs is not applied too.
            services.RemoveAll<DbContextOptions<GameTrackerDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<GameTrackerDbContext>>();
            services.AddDbContext<GameTrackerDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
