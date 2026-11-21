using System.Text;
using System.Text.Json;

namespace Openspace.Infrastructure.Shiori;

/// <summary>
/// Reads Shiori's answer from Claude Code's stream (<c>stream-json</c> with partial messages), line
/// by line: the pieces of text as they are written, and how the run ended. Without partial messages
/// each whole message counts as one piece, so the answer is the same either way.
/// </summary>
internal sealed class ShioriStream
{
    private readonly StringBuilder _answer = new();
    private bool _messageStreamed;
    private bool _messageStarted;

    /// <summary>Everything Shiori wrote in this run, its messages one after the other.</summary>
    public string Answer => _answer.ToString();

    /// <summary>The run's last words as Claude Code reports them, when it ended well.</summary>
    public string? Result { get; private set; }

    /// <summary>Why the run stopped, when it ended badly.</summary>
    public string? Error { get; private set; }

    public bool Ended { get; private set; }

    /// <returns>The new text this line brings, in order; nothing for lines about anything else.</returns>
    public IReadOnlyList<string> Read(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart()[0] != '{')
        {
            return [];
        }

        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        switch (Text(root, "type"))
        {
            case "stream_event" when root.TryGetProperty("event", out var e):
                return FromEvent(e);
            case "assistant":
                return FromMessage(root);
            case "result":
                Ended = true;
                var result = Text(root, "result");
                if (root.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True)
                {
                    Error = $"Claude Code stopped: {result ?? Text(root, "subtype") ?? "unknown error"}";
                }
                else
                {
                    Result = result?.Trim();
                }

                return [];
            default:
                return [];
        }
    }

    private List<string> FromEvent(JsonElement e)
    {
        switch (Text(e, "type"))
        {
            case "message_start":
                _messageStarted = true;
                _messageStreamed = false;
                return [];
            case "content_block_delta" when e.TryGetProperty("delta", out var delta) && Text(delta, "type") == "text_delta" && Text(delta, "text") is { Length: > 0 } text:
                _messageStreamed = true;
                return [Add(text)];
            default:
                return [];
        }
    }

    /// <summary>A whole message: its text counts only when it was not streamed piece by piece already.</summary>
    private List<string> FromMessage(JsonElement root)
    {
        var streamed = _messageStreamed;
        _messageStreamed = false;
        _messageStarted = false;
        if (streamed || !root.TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var pieces = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            if (Text(part, "type") == "text" && Text(part, "text") is { Length: > 0 } text)
            {
                _messageStarted = true;
                pieces.Add(Add(text));
                _messageStarted = false;
            }
        }

        return pieces;
    }

    /// <summary>Adds text to the answer; a new message after an earlier one starts a new paragraph.</summary>
    private string Add(string text)
    {
        var piece = _messageStarted && _answer.Length > 0 ? "\n\n" + text : text;
        _messageStarted = false;
        _answer.Append(piece);
        return piece;
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
