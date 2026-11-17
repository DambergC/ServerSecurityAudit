using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

var apiToken = Environment.GetEnvironmentVariable("AUDIT_API_TOKEN");
if (string.IsNullOrWhiteSpace(apiToken) || apiToken.Length < 32)
{
    throw new InvalidOperationException("AUDIT_API_TOKEN must be configured with at least 32 characters.");
}

var configuredConnectionString = Environment.GetEnvironmentVariable("AUDIT_SQL_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(configuredConnectionString))
{
    throw new InvalidOperationException("AUDIT_SQL_CONNECTION_STRING must be configured.");
}

var sqlConnectionString = new SqlConnectionStringBuilder(configuredConnectionString)
{
    Encrypt = true,
    TrustServerCertificate = false,
    PersistSecurityInfo = false,
    ApplicationName = "ServerSecurityAudit.Api"
}.ConnectionString;

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 2 * 1024 * 1024);
builder.Services.AddSingleton(new ApiTokenOptions(apiToken));
builder.Services.AddSingleton(new AuditReportStore(sqlConnectionString));
builder.Services
    .AddAuthentication("Bearer")
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>("Bearer", _ => { });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
    .AllowAnonymous();

app.MapGet("/health/ready", async (AuditReportStore store, CancellationToken cancellationToken) =>
{
    try
    {
        await store.CheckConnectionAsync(cancellationToken);
        return Results.Ok(new { status = "ready" });
    }
    catch (SqlException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
})
.AllowAnonymous();

app.MapPost("/api/audit-reports", async (JsonElement report, AuditReportStore store, CancellationToken cancellationToken) =>
{
    if (!IsValidReport(report, out var riskScore, out var riskLevel))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["report"] = ["Expected RiskScore (0-100), RiskLevel (A-F), and a Findings array."]
        });
    }

    var id = Guid.NewGuid();
    var receivedAtUtc = DateTime.UtcNow;
    await store.InsertAsync(id, receivedAtUtc, riskScore, riskLevel, report.GetRawText(), cancellationToken);
    return Results.Created($"/api/audit-reports/{id}", new { id, receivedAtUtc });
})
.RequireAuthorization();

app.MapGet("/api/audit-reports/{id:guid}", async (Guid id, AuditReportStore store, CancellationToken cancellationToken) =>
{
    var report = await store.GetAsync(id, cancellationToken);
    return report is null
        ? Results.NotFound()
        : Results.Content(report, "application/json");
})
.RequireAuthorization();

app.Run();

static bool IsValidReport(JsonElement report, out int riskScore, out string riskLevel)
{
    riskScore = -1;
    riskLevel = string.Empty;

    if (report.ValueKind != JsonValueKind.Object
        || !report.TryGetProperty("RiskScore", out var score)
        || !score.TryGetInt32(out riskScore)
        || riskScore is < 0 or > 100
        || !report.TryGetProperty("RiskLevel", out var level)
        || level.ValueKind != JsonValueKind.String
        || !report.TryGetProperty("Findings", out var findings)
        || findings.ValueKind != JsonValueKind.Array)
    {
        return false;
    }

    riskLevel = level.GetString() ?? string.Empty;
    var expectedRiskLevel = riskScore switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 65 => "C",
        >= 50 => "D",
        _ => "F"
    };

    return riskLevel == expectedRiskLevel
        && findings.EnumerateArray().All(finding => finding.ValueKind == JsonValueKind.Object);
}

public sealed record ApiTokenOptions(string Token);
