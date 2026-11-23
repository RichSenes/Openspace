using Microsoft.Extensions.Options;
using Openspace.Application.Voice;

namespace Openspace.Api.Endpoints;

/// <summary>
/// Talking to Shiori: the browser records, this turns the recording into text on this machine,
/// and the page sends the text as a message like a typed one.
/// </summary>
internal static class VoiceEndpoints
{
    public static void MapVoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var voice = app.MapGroup("/api/voice").WithTags("Voice");

        voice.MapGet("/status", (IListener listener, CancellationToken ct) => listener.StatusAsync(ct));

        voice.MapPost("/hear", async (HttpRequest request, IListener listener, IOptions<VoiceOptions> options, string? language, CancellationToken ct) =>
        {
            if (request.ContentLength is null or 0)
                return Results.Problem("Send what was said as the body.", statusCode: StatusCodes.Status400BadRequest);
            if (request.ContentLength > options.Value.MaxBytes)
                return Results.Problem("The recording is too long.", statusCode: StatusCodes.Status413PayloadTooLarge);
            try
            {
                return Results.Ok(await listener.HearAsync(request.Body, Languages.Choose(language), ct));
            }
            catch (ListenerException e)
            {
                return Results.Problem(e.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).DisableAntiforgery();
    }
}
