var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Hello, friend.
app.MapGet("/", () => "Openspace is running.");

app.Run();

/// <summary>Exposed for architecture and integration tests.</summary>
public partial class Program;
