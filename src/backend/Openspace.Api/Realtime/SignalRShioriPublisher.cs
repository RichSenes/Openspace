using Microsoft.AspNetCore.SignalR;
using Openspace.Application.Shiori;

namespace Openspace.Api.Realtime;

/// <summary>
/// Sends Shiori's sessions to browsers: <c>ShioriText(sessionId, chunk)</c> as the answer is
/// written, <c>ShioriMessage(sessionId, entry)</c> for each line saved in the transcript,
/// <c>ShioriBoard(sessionId, board)</c> with the whole whiteboard at each change and
/// <c>ShioriState(state)</c> for the mode, "answering" and the end.
/// </summary>
internal sealed class SignalRShioriPublisher(IHubContext<BuildingHub> hub) : IShioriPublisher
{
    public Task TextAsync(Guid sessionId, string chunk, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync("ShioriText", sessionId, chunk, cancellationToken);

    public Task MessageAsync(Guid sessionId, TranscriptEntry entry, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync("ShioriMessage", sessionId, entry, cancellationToken);

    public Task BoardAsync(Guid sessionId, IReadOnlyList<WhiteboardItem> board, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync("ShioriBoard", sessionId, board, cancellationToken);

    public Task StateAsync(ShioriState state, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync("ShioriState", state, cancellationToken);
}
