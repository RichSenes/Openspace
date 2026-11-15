using System.Globalization;
using System.Text.Json;
using Openspace.Application.Abstractions;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines;

/// <summary>What the chat answers in its place: the button that replaces yes and no, and a short notice.</summary>
public sealed record ChatAnswer(string Notice, string Button);

/// <summary>The answers given in the chat, for whatever listens to it.</summary>
public interface IChatAnswers
{
    /// <summary>The channels of enabled routines that ask in the chat: the only bots to listen to.</summary>
    Task<IReadOnlyList<string>> ChannelsAsync(CancellationToken cancellationToken);

    /// <returns>What to show in the chat; null when the button is not an answer to a proposal.</returns>
    Task<ChatAnswer?> AnswerAsync(string data, CancellationToken cancellationToken);

    /// <summary>Text the person wrote to a bot: an offer pasted whole goes to the job offers routine of that bot.</summary>
    /// <returns>What to answer in the chat; null when no routine of that bot takes offers.</returns>
    Task<string?> OfferAsync(string channel, string text, CancellationToken cancellationToken);
}

/// <summary>
/// The person answers a routine's proposal in their chat as they would in Openspace: the buttons
/// under the message say yes or no to it. Only the bots of routines that ask in the chat are
/// listened to, so a bot used elsewhere is never read.
/// </summary>
public sealed partial class ChatAnswers(IRoutineRepository routines, IEnumerable<IRoutineKind> kinds, ProposalCommands commands, OfferInbox inbox) : IChatAnswers
{
    private const string Prefix = "p:";

    /// <summary>Yes and no for a proposal, side by side.</summary>
    public static IReadOnlyList<OutboxButton> Buttons(Proposal proposal, string yes, string no)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var id = proposal.Id.Value.ToString("N", CultureInfo.InvariantCulture);
        return [new OutboxButton(yes, $"{Prefix}{id}:y"), new OutboxButton(no, $"{Prefix}{id}:n")];
    }

    /// <summary>The channels of enabled routines that ask in the chat: the only bots to listen to.</summary>
    public async Task<IReadOnlyList<string>> ChannelsAsync(CancellationToken cancellationToken)
    {
        var asking = kinds.Where(k => k.AsksInChat).Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
        var channels = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var office in await routines.OfficesWithRoutinesAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var routine in await routines.ListAsync(office, cancellationToken).ConfigureAwait(false))
            {
                if (routine.Enabled && asking.Contains(routine.Kind) && Setting(routine.Settings, "channel") is { } channel)
                {
                    channels.Add(channel.ToUpperInvariant());
                }
            }
        }

        return [.. channels];
    }

    /// <summary>Says yes or no to the proposal a button names.</summary>
    /// <returns>What to show in the chat; null when the button is not an answer to a proposal.</returns>
    public async Task<ChatAnswer?> AnswerAsync(string data, CancellationToken cancellationToken)
    {
        if (!data.StartsWith(Prefix, StringComparison.Ordinal) || data.Length < Prefix.Length + 34
            || !Guid.TryParseExact(data[Prefix.Length..^2], "N", out var guid) || data[^2] != ':' || data[^1] is not ('y' or 'n'))
        {
            return null;
        }

        var id = new ProposalId(guid);
        if (await routines.FindProposalAsync(id, cancellationToken).ConfigureAwait(false) is not { } proposal)
        {
            return new ChatAnswer(Words.English.Gone, "—");
        }

        var routine = await routines.FindAsync(proposal.RoutineId, cancellationToken).ConfigureAwait(false);
        var words = Words.For(routine is null ? null : Setting(routine.Settings, "language"));
        if (proposal.Status != ProposalStatus.Pending)
        {
            return new ChatAnswer(words.Already, Button(words, proposal));
        }

        if (data[^1] == 'y')
        {
            await commands.ApproveAsync(id, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await commands.RejectAsync(id, cancellationToken).ConfigureAwait(false);
        }

        var notice = proposal.Status switch
        {
            ProposalStatus.Rejected => words.Dropped,
            ProposalStatus.Failed => $"{words.Failed}: {proposal.Outcome}",
            _ => words.Taken,
        };
        return new ChatAnswer(notice.Length <= 190 ? notice : notice[..189] + "…", Button(words, proposal));
    }

    public async Task<string?> OfferAsync(string channel, string text, CancellationToken cancellationToken)
    {
        // The enabled job offers routine that writes with this bot.
        Routine? taker = null;
        foreach (var office in await routines.OfficesWithRoutinesAsync(cancellationToken).ConfigureAwait(false))
        {
            taker ??= (await routines.ListAsync(office, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(r => r.Enabled && r.Kind == "job-offers" && string.Equals(Setting(r.Settings, "channel"), channel, StringComparison.OrdinalIgnoreCase));
        }

        if (taker is null)
        {
            return null;
        }

        var words = Words.For(Setting(taker.Settings, "language"));
        text = text.Trim();
        if (text.Length < OfferInbox.LeastText)
        {
            return words.PasteWhole;
        }

        var link = Link().Match(text) is { Success: true } found ? found.Value.TrimEnd('.', ',', ')', ']') : null;
        await inbox.AddAsync(taker.Id, text.Length <= OfferInbox.MostText ? text : text[..OfferInbox.MostText], link, cancellationToken).ConfigureAwait(false);
        return words.Received;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"https?://\S+")]
    private static partial System.Text.RegularExpressions.Regex Link();

    private static string Button(Words words, Proposal proposal) => proposal.Status switch
    {
        ProposalStatus.Rejected => words.DroppedButton,
        ProposalStatus.Failed => words.FailedButton,
        _ => words.TakenButton,
    };

    private static string? Setting(string settings, string name)
    {
        using var document = JsonDocument.Parse(settings);
        return document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
    }

    private sealed record Words(
        string Taken, string TakenButton, string Dropped, string DroppedButton, string Failed, string FailedButton, string Already, string Gone, string Received, string PasteWhole)
    {
        public static readonly Words English = new(
            "Noted: it interests you. Its CV will answer here.", "✅ Interested", "Dropped: it will not come back.", "❌ Dropped",
            "It could not be done", "⚠️ Not done", "Already decided.", "This proposal is no longer in Openspace.",
            "📥 Got it: the offer is being judged and comes back here with its buttons.",
            "Paste the whole offer, its text and not only the link: a site that turns readers away cannot be opened.");

        private static readonly Words Italian = new(
            "Segnata: ti interessa. Il CV arriverà in risposta qui.", "✅ Ti interessa", "Scartata: non tornerà.", "❌ Scartata",
            "Non è riuscito", "⚠️ Non riuscito", "Già decisa.", "Questa proposta non è più in Openspace.",
            "📥 Ricevuta: la giudico e torna qui con i suoi bottoni.",
            "Incolla tutta l'offerta, il testo e non solo il link: un sito che chiude le porte ai lettori non si può aprire.");

        public static Words For(string? language) => language?.Trim().ToUpperInvariant() is "ITALIAN" or "ITALIANO" ? Italian : English;
    }
}
