using System.Text.Json.Serialization;
using GameTracker.Api.Data;
using GameTracker.Api.Games;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<GameTrackerDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("GameTracker")));
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<GameTrackerDbContext>().Database.Migrate();
}

// In Development, minimal APIs throw BadHttpRequestException for unreadable bodies;
// keep its 400 instead of letting the exception handler report a 500.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGamesEndpoints();

app.Run();

public partial class Program;
