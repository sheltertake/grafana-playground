using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

builder.Services.AddHttpClient("backend", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["BACKEND_URL"] ?? "http://backend:8080");
});

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());

builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeFormattedMessage = true;
    o.IncludeScopes = true;
    o.AddOtlpExporter();
});

var app = builder.Build();

app.UseCors();

app.UseExceptionHandler(exApp => exApp.Run(async ctx =>
{
    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsync("Internal Server Error");
}));

app.MapGet("/", () => "dotnet-app is running");

app.MapGet("/weather", (ILogger<Program> logger) =>
{
    logger.LogInformation("Fetching weather forecasts");
    var summaries = new[] { "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot" };
    var forecasts = Enumerable.Range(1, 5).Select(i => new
    {
        Date = DateTime.UtcNow.AddDays(i).ToString("yyyy-MM-dd"),
        TemperatureC = Random.Shared.Next(-20, 55),
        Summary = summaries[Random.Shared.Next(summaries.Length)]
    });
    return Results.Ok(forecasts);
});

app.MapGet("/slow", async (ILogger<Program> logger) =>
{
    var delay = Random.Shared.Next(200, 2000);
    logger.LogInformation("Slow endpoint called, waiting {DelayMs}ms", delay);
    await Task.Delay(delay);
    return Results.Ok(new { message = "done", delayMs = delay });
});

app.MapGet("/chain", async (IHttpClientFactory factory, ILogger<Program> logger) =>
{
    logger.LogInformation("Chain request started, calling backend");
    var client = factory.CreateClient("backend");
    var data = await client.GetStringAsync("/data");
    logger.LogInformation("Chain request completed");
    return Results.Ok(new { source = "dotnet-app", backend = data });
});

app.MapGet("/chain/slow", async (IHttpClientFactory factory, ILogger<Program> logger) =>
{
    logger.LogInformation("Chain slow request started");
    var client = factory.CreateClient("backend");
    var data = await client.GetStringAsync("/slow-data");
    return Results.Ok(new { source = "dotnet-app", backend = data });
});

app.MapGet("/error", (ILogger<Program> logger) =>
{
    logger.LogError("Simulated error triggered");
    throw new InvalidOperationException("Simulated error for observability testing");
});

app.Run();
