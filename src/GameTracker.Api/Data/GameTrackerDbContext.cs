using GameTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace GameTracker.Api.Data;

public class GameTrackerDbContext(DbContextOptions<GameTrackerDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();

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
