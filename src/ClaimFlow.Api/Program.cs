using System.Diagnostics;
using System.Text.Json.Serialization;
using ClaimFlow.Api.Data;
using ClaimFlow.Api.Infrastructure;
using ClaimFlow.Api.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging: readable text locally, one JSON object per line everywhere else (App Service log stream / Log Analytics).
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(json =>
    {
        json.UseUtcTimestamp = true;
        json.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    });
}

// ---- Kestrel: the API only ever receives small JSON bodies, and responses don't advertise the server software.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 16 * 1024;
    kestrel.AddServerHeader = false;
});

// ---- MVC, JSON and error responses
builder.Services
    .AddControllers()
    .AddJsonOptions(json =>
    {
        json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // Report malformed JSON as "invalid value" without echoing .NET type names back to the client.
        json.AllowInputFormatterExceptionMessages = false;
    });
builder.Services.AddProblemDetails(problems => problems.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

// ---- OpenAPI document (served at /openapi/v1.json) and the Scalar reference UI (/docs)
builder.Services.AddOpenApi(openApi => openApi.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "CLAIMFLOW API";
    document.Info.Version = "v1";
    document.Info.Description =
        "Insurance claims management demo. Independent, fictional portfolio project; not an official If Forsikring product. " +
        "All data is synthetic and resets nightly.";
    return Task.CompletedTask;
}));

// ---- Data, demo safety and application services
builder.Services.AddClaimFlowDatabase(builder.Configuration);
builder.Services.AddClaimFlowRateLimiting(builder.Configuration);
builder.Services.AddOptions<DemoOptions>()
    .Bind(builder.Configuration.GetSection(DemoOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IClaimReferenceGenerator, ClaimReferenceGenerator>();
builder.Services.AddScoped<ClaimService>();
builder.Services.AddScoped<PolicyService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<DemoDataService>();
builder.Services.AddSingleton<DemoResetGate>();
builder.Services.AddHostedService<DemoResetBackgroundService>();

builder.Services.AddHealthChecks().AddDbContextCheck<ClaimFlowDbContext>("database");

var app = builder.Build();

// ---- HTTP pipeline (order matters)
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseClaimFlowSecurityHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseDemoResetGate(); // nightly reset happens before the first API request after 03:00, even after the app slept

app.MapOpenApi();
// Keep the docs page self-contained: no external fonts, telemetry, AI agent, MCP or "deploy" promos.
app.MapScalarApiReference("/docs", scalar => scalar
    .WithTitle("CLAIMFLOW API")
    .DisableDefaultFonts()
    .DisableTelemetry()
    .DisableAgent()
    .DisableMcp()
    .HideDeveloperTools()
    .HideClientButton());

app.MapControllers();
app.MapHealthChecks("/health");

// Unknown /api routes return a ProblemDetails 404; every other unknown path goes to the React app's router.
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Endpoint not found"));
app.MapFallbackToFile("index.html");

await DatabaseInitializer.InitializeAsync(app.Services);
await app.Services.GetRequiredService<DemoResetGate>().EnsureFreshAsync(CancellationToken.None);
await app.RunAsync();

/// <summary>Entry point; public so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
