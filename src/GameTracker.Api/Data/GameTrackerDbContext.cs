using GameTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace GameTracker.Api.Data;

public class GameTrackerDbContext(DbContextOptions<GameTrackerDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();

    /// <summary>
    /// Seeding is registered here rather than in Program.cs so that every configuration of the
    /// context, including the in-memory one the tests use, seeds the same way.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder
            .UseSeeding(GameSeeder.Seed)
            .UseAsyncSeeding(GameSeeder.SeedAsync);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Game>(game =>
        {
            game.Property(g => g.Title)
                .IsRequired()
                .HasMaxLength(Game.TitleMaxLength)
                .UseCollation("NOCASE");

            game.Property(g => g.Platform).HasConversion<string>();
            game.Property(g => g.Status).HasConversion<string>();
        });
    }
}
