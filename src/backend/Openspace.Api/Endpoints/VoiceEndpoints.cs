using Microsoft.Extensions.Options;
using Openspace.Application.LocalModels;
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

        voice.MapPost("/speak/wake", async (ISpeaker speaker, GpuArbiter gpu, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await speaker.WakeAsync(ct));
            }
            catch (SpeakerException e)
            {
                return Unavailable(e, gpu);
            }
        });

        voice.MapPost("/speak", async (SpeakRequest request, ISpeaker speaker, GpuArbiter gpu, IOptions<VoiceOptions> options, CancellationToken ct) =>
        {
            if (Refused(request, options.Value) is { } refused)
                return refused;
            try
            {
                return Results.File(await speaker.SpeakAsync(request.Text!.Trim(), Languages.Choose(request.Language) ?? "it", ct), "audio/wav");
            }
            catch (SpeakerException e)
            {
                return Unavailable(e, gpu);
            }
        });

        // Her voice as it is made (16-bit mono PCM, rate in X-Sample-Rate): the page starts playing
        // the first words while the rest of the sentence is still being generated.
        voice.MapPost("/speak/stream", async (SpeakRequest request, HttpContext context, ISpeaker speaker, GpuArbiter gpu, IOptions<VoiceOptions> options, CancellationToken ct) =>
        {
            if (Refused(request, options.Value) is { } refused)
                return refused;
            SpokenAudio spoken;
            try
            {
                spoken = await speaker.StreamAsync(request.Text!.Trim(), Languages.Choose(request.Language) ?? "it", ct);
            }
            catch (SpeakerException e)
            {
                return Unavailable(e, gpu);
            }

            await using (spoken)
            {
                context.Response.ContentType = "audio/pcm";
                context.Response.Headers["X-Sample-Rate"] = spoken.SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Response.Headers.CacheControl = "no-store";
                var buffer = new byte[16384];
                try
                {
                    int read;
                    while ((read = await spoken.Audio.ReadAsync(buffer, ct)) > 0)
                    {
                        await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
                        await context.Response.Body.FlushAsync(ct);
                    }
                }
                catch (Exception e) when (e is OperationCanceledException or IOException or HttpRequestException)
                {
                    // The page stopped her, or the voice stopped mid-sentence: what was sent is what she said.
                }
            }

            return Results.Empty;
        });
    }

    private static IResult? Refused(SpeakRequest request, VoiceOptions options)
    {
        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            return Results.Problem("Give her something to say.", statusCode: StatusCodes.Status400BadRequest);
        if (text.Length > options.MaxSpokenChars)
            return Results.Problem($"She says at most {options.MaxSpokenChars} characters at once.", statusCode: StatusCodes.Status413PayloadTooLarge);
        return null;
    }

    // Busy: the graphics card is full (image generation, training...), so she writes instead of
    // speaking; a local model loaded but unused is unloaded for her at the next look.
    private static IResult Unavailable(SpeakerException e, GpuArbiter gpu)
    {
        if (e.Busy)
            gpu.WantRoom("Shiori's voice");
        return Results.Problem(e.Message, statusCode: StatusCodes.Status503ServiceUnavailable,
            title: e.Busy ? "Her voice is resting" : "Her voice is unavailable",
            extensions: new Dictionary<string, object?> { ["busy"] = e.Busy });
    }

    /// <summary>What Shiori says, and in which of her three languages.</summary>
    public sealed record SpeakRequest(string? Text, string? Language);
}
