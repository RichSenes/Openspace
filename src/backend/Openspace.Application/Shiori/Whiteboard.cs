namespace Openspace.Application.Shiori;

/// <summary>What Shiori can do to an item of the whiteboard.</summary>
public enum WhiteboardAction
{
    Add,

    /// <summary>To another column, another place in its column, or both.</summary>
    Move,

    Done,
    Remove,
}

/// <summary>One change to the whiteboard, as the tool receives it.</summary>
/// <param name="Item">The item's id; for every action but add.</param>
/// <param name="Position">For move: where in its column, 1 the top.</param>
public sealed record WhiteboardChange(
    WhiteboardAction Action, string? Item = null, WhiteboardKind? Kind = null, string? Text = null, int? Position = null, string? Office = null);

/// <summary>
/// The rules of the whiteboard. Items keep their order in one list; a column is the items of its
/// kind, in that order, so moving within a column only reorders those.
/// </summary>
public static class Whiteboard
{
    /// <summary>A board this full is a sign the session wants a summary, not more notes.</summary>
    public const int MaxItems = 200;

    public const int MaxText = 500;

    /// <returns>The item added or changed; the one removed, for remove.</returns>
    public static WhiteboardItem Apply(ShioriSession session, WhiteboardChange change)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(change);
        var board = session.Board;
        if (change.Action == WhiteboardAction.Add)
        {
            if (change.Kind is not { } kind)
            {
                throw new RejectedException("whiteboard-kind", "Say the item's kind: idea, doubt, decision or task.");
            }

            if (board.Count >= MaxItems)
            {
                throw new RejectedException("whiteboard-full", $"The whiteboard holds {MaxItems} items: remove or merge some first.");
            }

            var added = new WhiteboardItem($"w{session.NextItem++}", kind, Text(change.Text), Office: Blank(change.Office));
            board.Add(added);
            return added;
        }

        var index = board.FindIndex(i => string.Equals(i.Id, change.Item?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new RejectedException("whiteboard-item", $"There is no item \"{change.Item}\" on the whiteboard: items are named w1, w2…");
        }

        var item = board[index];
        switch (change.Action)
        {
            case WhiteboardAction.Done:
                board[index] = item with { Done = true };
                return board[index];
            case WhiteboardAction.Remove:
                board.RemoveAt(index);
                return item;
            default:
                var moved = item with
                {
                    Kind = change.Kind ?? item.Kind,
                    Text = change.Text is null ? item.Text : Text(change.Text),
                    Office = change.Office is null ? item.Office : Blank(change.Office),
                };
                board.RemoveAt(index);
                board.Insert(Place(board, moved.Kind, change.Position, index, moved.Kind == item.Kind), moved);
                return moved;
        }
    }

    /// <summary>
    /// Where an item goes in the one list: before the item now at that place of its column, after
    /// the column's last otherwise. Without a position it keeps its place when it stays in its column.
    /// </summary>
    private static int Place(List<WhiteboardItem> board, WhiteboardKind kind, int? position, int was, bool sameKind)
    {
        if (position is null && sameKind)
        {
            return was;
        }

        var column = board.Select((item, at) => (item, at)).Where(x => x.item.Kind == kind).Select(x => x.at).ToList();
        if (position is null)
        {
            return column.Count == 0 ? board.Count : column[^1] + 1;
        }

        var wanted = Math.Clamp(position.Value, 1, column.Count + 1);
        return wanted <= column.Count ? column[wanted - 1] : column.Count == 0 ? board.Count : column[^1] + 1;
    }

    private static string Text(string? text)
    {
        var trimmed = text?.ReplaceLineEndings(" ").Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new RejectedException("empty-text", "A whiteboard item needs a text.");
        }

        return trimmed.Length <= MaxText ? trimmed : trimmed[..MaxText];
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
