using System.Text.Json.Serialization;
using GameTracker.Api.Auth;
using GameTracker.Api.Data;
using GameTracker.Api.Games;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<GameTrackerDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("GameTracker")));
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Adds an Authorize button to Swagger UI that sends the key on every request.
    options.AddSecurityDefinition(ApiKeyAuthOptions.Scheme, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = ApiKeyAuthOptions.HeaderName,
        Description = "The owner's API key. Not needed when Auth:Enabled is false.",
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(ApiKeyAuthOptions.Scheme, document)] = [],
    });
});
builder.Services.AddApiKeyAuth(builder.Configuration);

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

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGamesEndpoints();

app.Run();

public partial class Program;
