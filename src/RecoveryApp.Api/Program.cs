using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RecoveryApp.Api.Endpoints;
using RecoveryApp.Api.Extensions;
using RecoveryApp.Application;
using RecoveryApp.Infrastructure;
using RecoveryApp.Infrastructure.Persistence;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices();

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Development convenience only. Deployments run `dotnet ef database update` as a release step
    // so that schema changes are reviewed and rolled forward deliberately.
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    DatabaseInitializer initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();

    // A missing database must not stop the host: the API docs stay browsable while Postgres is
    // still starting, and /healthz reports the outage instead of the process dying at boot.
    await initializer.TryMigrateAndSeedAsync().ConfigureAwait(false);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference();

app.MapHealthChecks("/healthz", new HealthCheckOptions { ResponseWriter = WriteHealthResponseAsync })
    .WithTags("Diagnostics")
    .AllowAnonymous();

app.MapAuthEndpoints();
app.MapContentEndpoints();
app.MapSyncEndpoints();
app.MapUserEndpoints();

await app.RunAsync().ConfigureAwait(false);

static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    return context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            durationMs = entry.Value.Duration.TotalMilliseconds,
            description = entry.Value.Description,
        }),
    }));
}

/// <summary>Entry point marker, exposed so integration tests can drive the host with WebApplicationFactory.</summary>
public partial class Program;
