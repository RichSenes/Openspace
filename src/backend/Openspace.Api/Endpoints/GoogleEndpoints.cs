using System.Net;
using Openspace.Application.Google;

namespace Openspace.Api.Endpoints;

/// <summary>Connecting the person's Google accounts: Google sends the browser back here with a code.</summary>
internal static class GoogleEndpoints
{
    public const string CallbackPath = "/api/google/callback";

    public static void MapGoogleEndpoints(this IEndpointRouteBuilder app)
    {
        var google = app.MapGroup("/api/google").WithTags("Google");
        google.MapGet("/", (IGoogleAccounts accounts, CancellationToken ct) => accounts.StatusAsync(ct));

        // Where to send the person: Google comes back to this same address, which must be this computer
        // (by its number, as Google prefers it for desktop apps).
        google.MapPost("/connect", (HttpRequest request, IGoogleAccounts accounts) =>
        {
            try
            {
                var host = request.Host.Host == "localhost" ? "127.0.0.1" : request.Host.Host;
                var port = request.Host.Port is { } p ? $":{p}" : string.Empty;
                return Results.Ok(new { url = accounts.BeginConnect(new Uri($"{request.Scheme}://{host}{port}{CallbackPath}")).AbsoluteUri });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        google.MapGet("/callback", async (string? state, string? code, string? error, IGoogleAccounts accounts, CancellationToken ct) =>
        {
            if (error is not null || string.IsNullOrEmpty(code))
            {
                return Page("Not connected", error == "access_denied" ? "You said no to Google: nothing was connected." : $"Google did not connect the account ({error ?? "no code"}).");
            }

            try
            {
                var email = await accounts.FinishConnectAsync(state ?? string.Empty, code, ct);
                return Page("Connected", $"{email} is connected to Openspace. You can close this tab.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
            {
                return Page("Not connected", ex.Message);
            }
        });

        google.MapDelete("/accounts/{email}", async (string email, IGoogleAccounts accounts, CancellationToken ct) =>
        {
            await accounts.DisconnectAsync(email, ct);
            return Results.NoContent();
        });
    }

    /// <summary>A small page for the browser tab Google sent back: what happened, in words.</summary>
    private static IResult Page(string title, string text) => Results.Content(
        $"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{title} · Openspace</title>
        <style>body{"{"}margin:0;min-height:100vh;display:grid;place-content:center;gap:8px;padding:16px;background:#14161c;color:#ece8df;font:16px system-ui,sans-serif{"}"}h1{"{"}margin:0;font-size:20px{"}"}p{"{"}margin:0;max-width:40ch;color:#a3a8b6{"}"}</style>
        </head><body><h1>{title}</h1><p>{WebUtility.HtmlEncode(text)}</p></body></html>
        """,
        "text/html");
}
