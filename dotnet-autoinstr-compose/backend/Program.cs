var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => "backend is running");

app.MapGet("/data", (ILogger<Program> logger) =>
{
    logger.LogInformation("Backend data requested");
    return Results.Ok(new
    {
        service = "backend",
        value = Random.Shared.Next(1, 100),
        timestamp = DateTime.UtcNow
    });
});

app.MapGet("/slow-data", async (ILogger<Program> logger) =>
{
    var delay = Random.Shared.Next(200, 1500);
    logger.LogInformation("Backend slow-data requested, waiting {DelayMs}ms", delay);
    await Task.Delay(delay);
    return Results.Ok(new
    {
        service = "backend",
        value = Random.Shared.Next(1, 100),
        delayMs = delay,
        timestamp = DateTime.UtcNow
    });
});

app.Run();
