namespace Openspace.Application.Shiori;

/// <summary>How Shiori works in a session; the person switches it deliberately, and it always shows.</summary>
public enum ShioriMode
{
    /// <summary>Asks and suggests, creates nothing: it only reads the brain and fills the whiteboard.</summary>
    Brainstorm,

    /// <summary>May also propose decisions and rules, and accept proposals the rules let it accept.</summary>
    Decide,

    /// <summary>May also turn the session into plans and tasks, through each office's supervisor, for approval.</summary>
    Organise,
}

/// <summary>Who an entry of the transcript comes from.</summary>
public enum TranscriptRole
{
    Person,
    Shiori,

    /// <summary>The person switched mode: kept in the transcript so a reader knows what Shiori was allowed to do.</summary>
    Mode,

    /// <summary>An answer that could not be had, with the reason.</summary>
    Failed,

    /// <summary>The session ended.</summary>
    Ended,
}

/// <summary>One line of the conversation.</summary>
/// <param name="Mode">The mode in force when it was said.</param>
public sealed record TranscriptEntry(TranscriptRole Role, string Text, ShioriMode Mode, DateTimeOffset At);

/// <summary>What a whiteboard item is: the columns of the board.</summary>
public enum WhiteboardKind
{
    Idea,
    Doubt,
    Decision,
    Task,
}

/// <summary>One sticky note of the whiteboard.</summary>
/// <param name="Id">Short and stable within the session (<c>w1</c>, <c>w2</c>…), so Shiori can name it in later calls.</param>
/// <param name="Office">The office it is about, when it is about one: a decision proposed from it is scoped there.</param>
/// <param name="Note">The proposal made from it, once there is one.</param>
public sealed record WhiteboardItem(string Id, WhiteboardKind Kind, string Text, bool Done = false, string? Office = null, string? Note = null);

/// <summary>
/// A conversation with Shiori, kept in Openspace's database: the transcript, the whiteboard, the
/// mode and the id of the engine's own conversation, so it resumes after a restart. Its note in
/// <c>Sessions/</c> is a readable copy written from this.
/// </summary>
public sealed class ShioriSession
{
    public required Guid Id { get; init; }

    public required string Title { get; set; }

    /// <summary>How the person is named in the note's transcript.</summary>
    public required string Person { get; init; }

    public ShioriMode Mode { get; set; } = ShioriMode.Brainstorm;

    /// <summary>The engine conversation's id, chosen by Openspace before the first answer so a restart finds it.</summary>
    public required string RuntimeSessionId { get; set; }

    /// <summary>Whether the engine has that conversation already: later answers resume it.</summary>
    public bool RuntimeStarted { get; set; }

    /// <summary>
    /// The secret that names this session to Openspace's tools. Not the id: ids travel in pages and
    /// hub messages, and anyone holding one could otherwise act as Shiori.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>The session's note in the vault.</summary>
    public required string NotePath { get; init; }

    public List<TranscriptEntry> Transcript { get; init; } = [];

    public List<WhiteboardItem> Board { get; init; } = [];

    /// <summary>The number of the next whiteboard item.</summary>
    public int NextItem { get; set; } = 1;

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public bool Ended => EndedAt is not null;
}

/// <summary>A session as pages see it: everything but the tools' secret.</summary>
/// <param name="Answering">Whether an answer is being written right now.</param>
public sealed record ShioriSessionView(
    Guid Id,
    string Title,
    ShioriMode Mode,
    string Note,
    IReadOnlyList<TranscriptEntry> Transcript,
    IReadOnlyList<WhiteboardItem> Board,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? EndedAt,
    bool Answering)
{
    public static ShioriSessionView From(ShioriSession session, bool answering)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new(
            session.Id, session.Title, session.Mode, session.NotePath, [.. session.Transcript], [.. session.Board],
            session.CreatedAt, session.UpdatedAt, session.EndedAt, answering);
    }
}

/// <summary>A line of the session list.</summary>
public sealed record ShioriSessionSummary(
    Guid Id, string Title, ShioriMode Mode, string Note, int Messages, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? EndedAt, bool Answering);

/// <summary>Where a session stands, pushed whenever it changes: the page's mode switch and "thinking" sign.</summary>
/// <param name="Error">Why the last answer failed, when it did.</param>
public sealed record ShioriState(Guid SessionId, ShioriMode Mode, bool Answering, bool Ended, string? Error = null);

/// <summary>What ending a session left behind.</summary>
/// <param name="Proposed">The decisions proposed from the whiteboard, as note paths.</param>
public sealed record ShioriEnding(ShioriSessionView Session, IReadOnlyList<string> Proposed);
