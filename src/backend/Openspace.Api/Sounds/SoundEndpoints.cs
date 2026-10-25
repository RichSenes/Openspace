namespace Openspace.Api.Sounds;

internal static class SoundEndpoints
{
    public static void MapSoundEndpoints(this IEndpointRouteBuilder app)
    {
        var sounds = app.MapGroup("/api/sounds").WithTags("Sounds");
        sounds.MapGet("/", (SoundStore store) => store.Read());
        sounds.MapPut("/", (SoundConfig config, SoundStore store) => store.Save(config));
        sounds.MapPost("/files", async (IFormFile file, SoundStore store, CancellationToken ct) =>
            Results.Ok(new { name = await store.AddFileAsync(file, ct) }))
            .DisableAntiforgery();
        sounds.MapGet("/files/{name}", (string name, SoundStore store) =>
            store.Find(name) is { } found ? Results.File(found.Path, found.Type) : Results.NotFound());
        sounds.MapDelete("/files/{name}", (string name, SoundStore store) =>
        {
            store.DeleteFile(name);
            return Results.NoContent();
        });
    }
}
