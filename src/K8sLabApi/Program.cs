using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "K8sLabApi")
    .WriteTo.Console());

// Readiness starts false; a background toggle flips it after warm-up,
// and the chaos endpoints can flip it off again to simulate a bad rollout.
var appState = new AppState();
builder.Services.AddSingleton(appState);

builder.Services.AddHealthChecks()
    .AddCheck("liveness", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck("readiness", () => appState.IsReady
        ? HealthCheckResult.Healthy("ready")
        : HealthCheckResult.Unhealthy("warming up or draining"), tags: new[] { "ready" });

var app = builder.Build();

app.UseSerilogRequestLogging();

// Prometheus /metrics scrape endpoint + built-in HTTP metrics middleware.
app.UseHttpMetrics();
app.MapMetrics(); // exposes /metrics

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapGet("/", () => Results.Ok(new
{
    service = "K8sLabApi",
    pod = Environment.GetEnvironmentVariable("POD_NAME") ?? Environment.MachineName,
    node = Environment.GetEnvironmentVariable("NODE_NAME") ?? "unknown",
    version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev"
}));

// A normal-ish business endpoint so you have something to load-test.
app.MapGet("/api/weather", () =>
{
    var summaries = new[] { "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching" };
    var rng = Random.Shared;
    var forecast = Enumerable.Range(1, 5).Select(index => new
    {
        Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
        TemperatureC = rng.Next(-20, 55),
        Summary = summaries[rng.Next(summaries.Length)]
    });
    return Results.Ok(forecast);
});

// ---------- Chaos endpoints: use these to trigger real incidents ----------
var chaos = app.MapGroup("/api/chaos");

// Pins a CPU core for N seconds. Combine with the HPA to watch it scale out.
chaos.MapPost("/cpu", (int seconds = 20) =>
{
    Log.Warning("Chaos: burning CPU for {Seconds}s", seconds);
    var sw = Stopwatch.StartNew();
    Parallel.For(0, Environment.ProcessorCount, _ =>
    {
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            _ = (int)Math.Sqrt(Random.Shared.NextDouble());
        }
    });
    return Results.Ok(new { message = $"burned CPU for {seconds}s" });
});

// Leaks memory on purpose. With a low container memory limit this ends in OOMKilled.
var leakedMemory = new List<byte[]>();
chaos.MapPost("/memory", (int megabytes = 50) =>
{
    Log.Warning("Chaos: allocating {MB}MB and holding it", megabytes);
    leakedMemory.Add(new byte[megabytes * 1024 * 1024]);
    return Results.Ok(new { message = $"allocated {megabytes}MB", totalChunks = leakedMemory.Count });
});
chaos.MapPost("/memory/reset", () =>
{
    leakedMemory.Clear();
    GC.Collect();
    return Results.Ok(new { message = "memory released" });
});

// Kills the process outright. Kubernetes will restart it -> watch CrashLoopBackOff
// if you call this repeatedly (or set the exit code to keep failing on startup).
chaos.MapPost("/crash", () =>
{
    Log.Fatal("Chaos: process is exiting on purpose");
    _ = Task.Run(async () =>
    {
        await Task.Delay(200);
        Environment.Exit(1);
    });
    return Results.Accepted(value: new { message = "process exiting in 200ms" });
});

// Sleeps past a typical probe/timeout window to simulate a hung dependency.
chaos.MapPost("/slow", async (int seconds = 10) =>
{
    Log.Warning("Chaos: sleeping for {Seconds}s", seconds);
    await Task.Delay(TimeSpan.FromSeconds(seconds));
    return Results.Ok(new { message = $"slept for {seconds}s" });
});

// Flips readiness off/on so you can watch the Service pull the pod out of rotation
// without the pod being restarted.
chaos.MapPost("/unready", () =>
{
    appState.IsReady = false;
    Log.Warning("Chaos: readiness forced to false");
    return Results.Ok(new { message = "pod will now fail readiness checks" });
});
chaos.MapPost("/ready", () =>
{
    appState.IsReady = true;
    return Results.Ok(new { message = "pod is ready again" });
});

// Warm-up delay so a fresh pod is briefly NotReady on startup, like a real app
// loading caches/connections - makes readiness gating visible during rollouts.
_ = Task.Run(async () =>
{
    await Task.Delay(TimeSpan.FromSeconds(5));
    appState.IsReady = true;
    Log.Information("Startup warm-up complete, marking ready");
});

app.Run();

class AppState
{
    public volatile bool IsReady = false;
}
