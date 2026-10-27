namespace Openspace.Application.Routines;

/// <param name="Id">The mailbox's own id of the message.</param>
/// <param name="From">As the header says it: "Name &lt;address&gt;".</param>
/// <param name="ReplyTo">Where an answer goes: the Reply-To header, or the sender.</param>
/// <param name="MessageId">The Message-ID header, so an answer joins the conversation.</param>
/// <param name="Text">The body as plain text, shortened.</param>
/// <param name="Bulk">Sent to a list (it says how to unsubscribe): newsletters, offers, notices.</param>
public sealed record MailMessage(
    string Id, string ThreadId, string From, string ReplyTo, string Subject, string MessageId, DateTimeOffset Received,
    string Snippet, string Text, bool Unread, bool Bulk);

/// <param name="Next">Where the next page starts; null after the last.</param>
public sealed record MailPage(IReadOnlyList<string> Ids, string? Next);

/// <summary>An answer to keep as a draft: the person reads it, changes it and sends it.</summary>
public sealed record MailReply(string ThreadId, string To, string Subject, string InReplyTo, string Body);

/// <summary>A person's mailbox, as routines use it: they read, label, archive and draft; they never send.</summary>
public interface IMailbox
{
    /// <summary>One page of the messages matching <paramref name="query"/>, newest first: their ids only, which is cheap.</summary>
    /// <param name="query">What to look for, as the mailbox's search box takes it: "in:inbox newer_than:2d".</param>
    /// <param name="page">Where the previous page left off; null for the first.</param>
    Task<MailPage> FindAsync(string account, string query, string? page, CancellationToken cancellationToken);

    Task<MailMessage> ReadAsync(string account, string messageId, CancellationToken cancellationToken);

    /// <summary>Puts a label on the message, making the label if needed, and takes it out of the inbox when asked.</summary>
    Task LabelAsync(string account, string messageId, string label, bool archive, CancellationToken cancellationToken);

    /// <returns>The draft's id.</returns>
    Task<string> DraftReplyAsync(string account, MailReply reply, CancellationToken cancellationToken);
}
