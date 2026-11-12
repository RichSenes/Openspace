using Microsoft.Extensions.Options;
using Openspace.Application.LocalModels;
using Openspace.Application.Routines;
using Openspace.Application.Routines.Kinds;

namespace Openspace.Api.Endpoints;

/// <summary>
/// What the routine forms lean on: a preview of the paper's layout, a test of a news feed,
/// the channels and the local models a routine may pick. Nothing here runs a routine.
/// </summary>
internal static class RoutineHelpEndpoints
{
    public sealed record PreviewRequest(string Settings);

    public sealed record FeedTestRequest(string Url);

    public sealed record FeedTestResult(bool Ok, int Count, IReadOnlyList<string> Titles, string? Problem);

    public sealed record ModelChoice(string Id, string Name);

    public static void MapRoutineHelpEndpoints(this IEndpointRouteBuilder app)
    {
        var help = app.MapGroup("/api").WithTags("Routines");

        // Sample text in the settings' layout; the page is shown in a sandboxed frame.
        help.MapPost("/routine-kinds/morning-paper/preview", (PreviewRequest request, TimeProvider clock) =>
        {
            try
            {
                var html = PaperPreview.Render(request.Settings, DateOnly.FromDateTime(clock.GetLocalNow().DateTime));
                return Results.Content(html, "text/html; charset=utf-8");
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
            {
                return Results.Problem(title: "No preview", detail: ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });

        help.MapPost("/feeds/test", async (FeedTestRequest request, IFeedReader reader, CancellationToken ct) =>
        {
            if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                return new FeedTestResult(false, 0, [], "This is not a web address: it starts with https://.");
            }

            try
            {
                var items = await reader.ReadAsync(url, url.Host, ct);
                return items.Count == 0
                    ? new FeedTestResult(false, 0, [], "The address answers, but no news came from it: it may be a web page rather than a feed.")
                    : new FeedTestResult(true, items.Count, [.. items.OrderByDescending(i => i.Published).Take(5).Select(i => i.Title)], null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return new FeedTestResult(false, 0, [], Explain(ex, url));
            }
        });

        help.MapGet("/routine-channels", (IRoutineOutbox outbox) => outbox.Channels());

        help.MapGet("/routine-models", (IOptionsMonitor<LocalModelOptions> options) =>
        {
            var local = options.CurrentValue;
            return local.Models
                .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                .Select(m => new ModelChoice($"{local.Provider}/{m.Id}", string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name))
                .ToList();
        });
    }

    /// <summary>Why a feed could not be read, in words for the person, with the technical reason after.</summary>
    internal static string Explain(Exception ex, Uri url) => ex switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized } =>
            $"{url.Host} refuses automatic readers. Look for another source with the same news.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } =>
            $"{url.Host} has nothing at this address: the feed may have moved. Look for \"RSS\" on the site.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } =>
            $"{url.Host} asks to slow down: try again in a few minutes.",
        HttpRequestException { StatusCode: { } code } => $"{url.Host} answered with an error ({(int)code}): it may be down for now.",
        HttpRequestException => $"{url.Host} could not be reached: check the address, or the site is down. ({ex.Message})",
        TaskCanceledException or TimeoutException => $"{url.Host} took too long to answer: try again later.",
        _ => $"This is not a feed Openspace can read. ({ex.Message})",
    };
}
