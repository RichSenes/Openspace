using Openspace.Application.LocalModels;
using Openspace.Application.Voice;

namespace Openspace.Infrastructure.Voice;

/// <summary>Shiori's voice as a guest on the graphics card: it gives its memory back to a model, and loads again when she next speaks.</summary>
public sealed class VoiceGuest(ISpeaker speaker) : IGpuGuest
{
    public string Name => "Shiori's voice";

    public Task<bool> MakeRoomAsync(TimeSpan quietFor, CancellationToken cancellationToken) => speaker.SleepAsync(quietFor, cancellationToken);
}
