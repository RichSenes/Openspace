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

        voice.MapPost("/hear", async (HttpRequest request, IListener listener, IOptions<VoiceOptions> options, string? language, bool? wake, CancellationToken ct) =>
        {
            if (request.ContentLength is null or 0)
                return Results.Problem("Send what was said as the body.", statusCode: StatusCodes.Status400BadRequest);
            if (request.ContentLength > options.Value.MaxBytes)
                return Results.Problem("The recording is too long.", statusCode: StatusCodes.Status413PayloadTooLarge);
            try
            {
                return Results.Ok(await listener.HearAsync(request.Body, Languages.Choose(language), wake ?? false, ct));
            }
            catch (ListenerException e)
            {
                return Results.Problem(e.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).DisableAntiforgery();

        voice.MapGet("/speak/status", (ISpeaker speaker, CancellationToken ct) => speaker.StatusAsync(ct));

        voice.MapPost("/speak", async (SpeakRequest request, ISpeaker speaker, IOptions<VoiceOptions> options, CancellationToken ct) =>
        {
            var text = request.Text?.Trim();
            if (string.IsNullOrEmpty(text))
                return Results.Problem("Give her something to say.", statusCode: StatusCodes.Status400BadRequest);
            if (text.Length > options.Value.MaxSpokenChars)
                return Results.Problem($"She says at most {options.Value.MaxSpokenChars} characters at once.", statusCode: StatusCodes.Status413PayloadTooLarge);
            try
            {
                return Results.File(await speaker.SpeakAsync(text, Languages.Choose(request.Language) ?? "it", ct), "audio/wav");
            }
            catch (SpeakerException e)
            {
                return Results.Problem(e.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
    }

    /// <summary>What Shiori says, and in which of her three languages.</summary>
    public sealed record SpeakRequest(string? Text, string? Language);
}
