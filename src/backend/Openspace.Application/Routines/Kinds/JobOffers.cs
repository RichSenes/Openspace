using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Openspace.Application.LocalModels;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// Job offers of the day, measured against the person's profile. The boards' newest offers
/// pass the person's own rules first (in Japan, a role they look for, visa, Japanese, salary);
/// a local model then reads each new one with the profile and says how well it fits and why,
/// and what the text says on Japanese and visas when the board does not. The good ones reach the chat
/// and wait in Openspace for "interested" or "no": an offer turned down never comes back, and
/// an offer seen once comes back only when it changes in substance, under its own link or
/// posted again under another. An offer the person brings, pasted into the inbox, is judged
/// the same way and always shown. A yes saves the offer in its own folder for the CV routines
/// of the office, which are asked to run and make a CV for it.
/// </summary>
public sealed class JobOffers(IEnumerable<IJobBoard> boards, ILocalChat chat, Abstractions.IRoutineRepository routines, RoutineRequests requests) : IRoutineKind
{
    /// <summary>Offers the person brings: they come from no board.</summary>
    public const string Brought = "brought";

    /// <summary>In an offer's folder, the offer itself, which the CV routine reads.</summary>
    public const string OfferFile = "offer.md";

    public const string InterestedAction = "interested";

    /// <summary>Offers by their link.</summary>
    public const string OfferPrefix = "job:";

    /// <summary>Offers by company and title, so the same job posted again under another link is known.</summary>
    public const string RolePrefix = "role:";

    public string Key => "job-offers";

    /// <summary>Each offer comes with yes and no in the chat.</summary>
    public bool AsksInChat => true;

    /// <summary>In an offer's folder, the chat message that proposed it, for its CV to answer.</summary>
    public const string MessageFile = "message.txt";

    public string Name => "Job offers";

    public string Description =>
        "Reads the newest job offers, keeps those that fit your rules, has a local model measure them against your profile and sends you the good ones, never twice.";

    public string DefaultSettings => """
        {
          "title": "Job offers",
          "channel": "",
          "language": "English",
          "model": "local/gemma",
          "profile": "~/Documents/CV/profile.md",
          "boards": ["japan-dev"],
          "roles": ["developer", "engineer", "programmer", "frontend", "front-end", "backend", "back-end", "full stack", "full-stack", "fullstack", "software", "エンジニア"],
          "visa": true,
          "japaneseUpTo": "conversational",
          "minSalary": 0,
          "cities": [],
          "most": 8,
          "fit": 3,
          "avoid": [],
          "inbox": "~/Documents/CV/inbox",
          "offers": "~/Documents/CV/offers"
        }
        """;

    /// <summary>Of each offer, the start the model reads.</summary>
    private const int Reading = 4000;

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = JobSettings.Read(context.Settings);
        var words = JobWords.For(settings.Language);
        var path = MorningPaper.Expand(settings.Profile);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"No profile at {settings.Profile}: write who you are and what you look for there, or change \"profile\".");
        }

        var profile = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var brought = await BroughtAsync(context, settings, words, profile, cancellationToken).ConfigureAwait(false);
        var offers = new List<JobOffer>();
        foreach (var choice in settings.Boards)
        {
            if (boards.FirstOrDefault(b => b.Reads(choice.Board)) is not { } board)
            {
                context.Write($"No reader for the board {choice.Board}: write it as \"japan-dev\" or as service and company, \"lever:acme\".");
                continue;
            }

            context.Progress($"Reading {choice.Board}");
            if (await ReadAsync(context, board, choice.Board, cancellationToken).ConfigureAwait(false) is { } found)
            {
                offers.AddRange(choice.AllInJapan ? found.Select(o => o with { InJapan = true }) : found);
            }
        }

        var kept = offers.Where(o => Passes(settings, o)).ToList();
        var waiting = new List<JobOffer>();
        foreach (var offer in kept.OrderByDescending(o => o.Posted))
        {
            if (waiting.Count < DeskReads && !waiting.Any(f => RoleKey(f) == RoleKey(offer))
                && await context.IsNewAsync(OfferKey(offer), Fingerprint(offer), cancellationToken).ConfigureAwait(false)
                && await context.IsNewAsync(RoleKey(offer), Fingerprint(offer), cancellationToken).ConfigureAwait(false))
            {
                waiting.Add(offer);
            }
        }

        // The person's own answers teach what they want; the desk picks the few worth a full reading.
        var decided = await DecidedAsync(context, cancellationToken).ConfigureAwait(false);
        var fresh = await DeskAsync(context, settings, profile, decided, waiting, cancellationToken).ConfigureAwait(false);
        context.Write($"{offers.Count} offers read, {kept.Count} fit your rules, {waiting.Count} new, {fresh.Count} to judge.");

        var good = new List<(JobOffer Offer, Judgement Judgement)>();
        var judged = 0;
        foreach (var (listed, i) in fresh.Select((o, i) => (o, i)))
        {
            context.Progress($"Measuring offer {i + 1} of {fresh.Count} against your profile");
            if (await JudgeAsync(context, settings, profile, listed, cancellationToken, decided).ConfigureAwait(false) is not { } judgement)
            {
                continue;
            }

            judged++;

            // What the board did not say, the text may: the rules hold for it too.
            var offer = listed with
            {
                Japanese = listed.Japanese == JapaneseLevel.Unknown ? Level(judgement.Japanese) : listed.Japanese,
                SponsorsVisa = listed.SponsorsVisa ?? Visa(judgement.Visa),
                SalaryMin = listed.SalaryMin ?? judgement.YearlyYen(judgement.SalaryLow),
                SalaryMax = listed.SalaryMax ?? judgement.YearlyYen(judgement.SalaryHigh is > 0 ? judgement.SalaryHigh : judgement.SalaryLow),
                Text = judgement.Details,
            };
            var title = Short($"{offer.Title} — {offer.Company}", 280);
            await context.MeetAsync(OfferKey(offer), Fingerprint(listed), title, cancellationToken).ConfigureAwait(false);
            await context.MeetAsync(RoleKey(offer), Fingerprint(listed), title, cancellationToken).ConfigureAwait(false);
            var state = judgement.Fit >= settings.Fit && Passes(settings, offer) ? SeenState.Shown : SeenState.Skipped;
            await context.MarkAsync(OfferKey(offer), state, cancellationToken).ConfigureAwait(false);
            await context.MarkAsync(RoleKey(offer), state, cancellationToken).ConfigureAwait(false);
            if (state == SeenState.Shown)
            {
                good.Add((offer, judgement));
            }
        }

        if (fresh.Count > 0 && judged == 0 && brought.Count == 0)
        {
            throw new InvalidOperationException("The local model could not judge any offer: they stay new for the next run.");
        }

        // The preferred cities first, in their order; then the better fit.
        good = [.. good.OrderBy(g => CityRank(settings, g.Offer)).ThenByDescending(g => g.Judgement.Fit)];
        var proposed = new List<(JobOffer Offer, Judgement Judgement, Proposal Proposal)>();
        foreach (var (offer, judgement) in good)
        {
            proposed.Add((offer, judgement, await ProposeAsync(context, words, offer, judgement, cancellationToken).ConfigureAwait(false)));
        }

        // What the person brought comes first: they asked for it.
        proposed = [.. brought, .. proposed];
        if (proposed.Count == 0)
        {
            return $"No new offers for you: {kept.Count} of {offers.Count} fit your rules, {judged} new ones judged.";
        }

        // A heading, then each offer in a message of its own with its yes and no: the proposal keeps
        // the message, so that the offer's CV can answer it.
        await context.PostAsync(Heading(settings, words, proposed.Count), cancellationToken).ConfigureAwait(false);
        foreach (var (offer, judgement, proposal) in proposed)
        {
            var message = Message(words, offer, judgement.Fit, judgement.Why, judgement.Gaps, judgement.Detail) with { Buttons = ChatAnswers.Buttons(proposal, words.Yes, words.No) };
            if (await context.PostAsync(message, cancellationToken).ConfigureAwait(false) is { } id)
            {
                proposal.Asked(id);
            }
        }

        return (brought.Count > 0 ? $"{brought.Count} brought offers judged; " : string.Empty)
            + $"{proposed.Count - brought.Count} new offers for you, of {judged} judged; {kept.Count} of {offers.Count} fit your rules.";
    }

    /// <summary>New offers the desk reads at most, by title: the newest first.</summary>
    private const int DeskReads = 150;

    /// <summary>Of the person's answers, how many recent ones teach the desk and the judge.</summary>
    private const int Remembered = 30;

    /// <summary>How long to wait before reading a board again when it could not be read: the network may be back.</summary>
    public TimeSpan RetryAfter { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>A board's offers; read again once after a pause when it fails, null when it fails twice.</summary>
    private async Task<IReadOnlyList<JobOffer>?> ReadAsync(RoutineContext context, IJobBoard board, string name, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await board.LatestAsync(name, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (attempt == 2)
                {
                    context.Write($"{name} could not be read ({ex.Message}).");
                    return null;
                }

                context.Write($"{name} could not be read ({ex.Message}): trying again shortly.");
                await Task.Delay(RetryAfter, context.Clock, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>What the person said of the offers shown to them, the newest answers first.</summary>
    /// <param name="Yes">Offers they wanted, "title — company".</param>
    /// <param name="No">Offers they turned down.</param>
    internal sealed record Decided(IReadOnlyList<string> Yes, IReadOnlyList<string> No);

    private async Task<Decided> DecidedAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        var seen = await routines.ListSeenAsync(context.Routine.Id, RolePrefix, cancellationToken).ConfigureAwait(false);
        List<string> Of(SeenState state) =>
            [.. seen.Where(s => s.State == state).OrderByDescending(s => s.ChangedAt).Take(Remembered).Select(s => s.Title)];
        return new Decided(Of(SeenState.Taken), Of(SeenState.Dismissed));
    }

    /// <summary>What the person does not want, and their past answers, for the desk and the judge.</summary>
    private static string Wishes(JobSettings settings, Decided? decided)
    {
        var text = new StringBuilder();
        if (settings.Avoid.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\nThe person does not want: {string.Join("; ", settings.Avoid)}.");
        }

        if (decided is { Yes.Count: > 0 })
        {
            text.Append(CultureInfo.InvariantCulture, $"\nOffers they said yes to:\n{string.Join('\n', decided.Yes.Select(t => $"- {t}"))}");
        }

        if (decided is { No.Count: > 0 })
        {
            text.Append(CultureInfo.InvariantCulture, $"\nOffers they said no to:\n{string.Join('\n', decided.No.Select(t => $"- {t}"))}");
        }

        return text.ToString();
    }

    /// <summary>
    /// The desk: the local model reads the titles of every new offer that meets the rules, with
    /// the person's profile, what they do not want and their past answers, and picks the few
    /// worth a full reading. With few offers, or without an answer, the newest go on.
    /// </summary>
    private async Task<List<JobOffer>> DeskAsync(
        RoutineContext context, JobSettings settings, string profile, Decided decided, List<JobOffer> waiting, CancellationToken cancellationToken)
    {
        if (waiting.Count <= settings.Most)
        {
            return waiting;
        }

        context.Progress($"Choosing {settings.Most} of {waiting.Count} new offers");
        var list = string.Join('\n', waiting.Select((o, i) =>
            $"[{i + 1}] {o.Title} — {o.Company}, {o.Location}{(o.Tags.Count > 0 ? $" ({string.Join(", ", o.Tags)})" : string.Empty)}"));
        try
        {
            var answer = await chat.AskAsync(context,
                new LocalAsk(
                    settings.Model,
                    $"""
                    You help one person find a job, choosing which offers deserve a full reading. Answer only with the JSON asked.
                    The person's profile:
                    {Short(profile, 4000)}
                    {Wishes(settings, decided)}
                    """,
                    $"""
                    The desk of job offers. New offers, by title, numbered:
                    {list}

                    Pick the {settings.Most} that most likely fit the person, the best first: the roles and skills they have and look for, like the offers they said yes to, unlike those they said no to and what they do not want.
                    Give the numbers in "picks".
                    """,
                    """{"type":"object","properties":{"picks":{"type":"array","items":{"type":"integer"}}},"required":["picks"]}""",
                    MaxTokens: 400, Temperature: 0.2),
                cancellationToken).ConfigureAwait(false);
            var picks = (JsonSerializer.Deserialize<DeskAnswer>(answer, JsonSerializerOptions.Web)?.Picks ?? [])
                .Where(n => n >= 1 && n <= waiting.Count).Distinct().Take(settings.Most).Select(n => waiting[n - 1]).ToList();
            if (picks.Count > 0)
            {
                context.Write($"The desk picked {picks.Count} of {waiting.Count} new offers.");
                return picks;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"The desk could not choose ({ex.Message}): the newest go on.");
        }

        return [.. waiting.Take(settings.Most)];
    }

    private sealed record DeskAnswer(List<int>? Picks);

    private static Task<Proposal> ProposeAsync(RoutineContext context, JobWords words, JobOffer offer, Judgement judgement, CancellationToken cancellationToken) =>
        context.ProposeAsync(
            InterestedAction,
            Short($"{offer.Title} — {offer.Company}, {offer.Location}", 200),
            Short($"{Facts(offer, words)} · {judgement.Fit}/5\n\n{judgement.Why}\n\n{words.Gaps}: {judgement.Gaps}\n\n{judgement.Detail.Plain(words)}\n\n{offer.Url}\n\n{words.Ask}".Replace("\n\n\n\n", "\n\n", StringComparison.Ordinal), 20_000),
            new OfferPayload(
                OfferKey(offer), RoleKey(offer), offer.Url.ToString(), offer.Title, offer.Company, offer.Location, Facts(offer, JobWords.For("English")), offer.Text ?? string.Empty,
                judgement.Detail.Plain(words)),
            cancellationToken);

    /// <summary>
    /// The offers the person pasted into the inbox, each judged and shown whatever the rules: the
    /// model also reads its title, company and place. A file judged leaves the inbox; one the
    /// model could not judge stays for the next run.
    /// </summary>
    private async Task<List<(JobOffer Offer, Judgement Judgement, Proposal Proposal)>> BroughtAsync(
        RoutineContext context, JobSettings settings, JobWords words, string profile, CancellationToken cancellationToken)
    {
        var inbox = MorningPaper.Expand(settings.Inbox);
        var files = Directory.Exists(inbox)
            ? Directory.GetFiles(inbox).Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToList()
            : [];
        var judged = new List<(JobOffer, Judgement, Proposal)>();
        foreach (var (file, i) in files.Select((f, i) => (f, i)))
        {
            context.Progress($"Measuring the offer you brought, {i + 1} of {files.Count}");
            var (link, text) = OfferInbox.Read(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false));
            var listed = new JobOffer(
                Brought, link ?? new Uri($"https://brought.invalid/{RoutineContext.Fingerprint(text)}"), string.Empty, string.Empty, string.Empty,
                JapaneseLevel.Unknown, null, null, null, null, string.Empty, string.Empty, [], context.Now, Text: text);
            if (await JudgeAsync(context, settings, profile, listed, cancellationToken).ConfigureAwait(false) is not { } judgement)
            {
                continue;
            }

            var offer = listed with
            {
                Title = Short(judgement.Title?.Trim() is { Length: > 0 } named ? named : words.Untitled, 200),
                Company = Short(judgement.Company?.Trim() is { Length: > 0 } company ? company : "?", 120),
                Location = Short(judgement.Location?.Trim() is { Length: > 0 } place ? place : "?", 120),
                Japanese = Level(judgement.Japanese),
                SponsorsVisa = Visa(judgement.Visa),
                SalaryMin = judgement.YearlyYen(judgement.SalaryLow),
                SalaryMax = judgement.YearlyYen(judgement.SalaryHigh is > 0 ? judgement.SalaryHigh : judgement.SalaryLow),
            };
            await context.MeetAsync(OfferKey(offer), RoutineContext.Fingerprint(text), Short($"{offer.Title} — {offer.Company}", 280), cancellationToken).ConfigureAwait(false);
            await context.MarkAsync(OfferKey(offer), SeenState.Shown, cancellationToken).ConfigureAwait(false);
            var proposal = await ProposeAsync(context, words, offer, judgement, cancellationToken).ConfigureAwait(false);
            File.Delete(file);
            judged.Add((offer, judgement, proposal));
        }

        return judged;
    }

    public async Task<string> CarryOutAsync(Proposal proposal, RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);
        var payload = Payload(proposal);
        await context.MarkAsync(payload.OfferKey, SeenState.Taken, cancellationToken).ConfigureAwait(false);
        await context.MarkAsync(payload.RoleKey, SeenState.Taken, cancellationToken).ConfigureAwait(false);

        // The offer in its own folder, where the CV routines make a CV for it.
        var folder = Path.Combine(MorningPaper.Expand(JobSettings.Read(context.Settings).Offers), FolderName(payload.Company, payload.Title));
        Directory.CreateDirectory(folder);
        if (proposal.MessageId is { } message)
        {
            await File.WriteAllTextAsync(Path.Combine(folder, MessageFile), message.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        }

        await File.WriteAllTextAsync(
            Path.Combine(folder, OfferFile),
            $"# {payload.Title}\n\nCompany: {payload.Company}\nPlace: {payload.Location}\nLink: {payload.Url}\nFacts: {payload.Facts}\n\n{payload.Text}\n"
            + (payload.Fit.Length > 0 ? $"\n## How the person fits\n\n{payload.Fit}\n" : string.Empty),
            cancellationToken).ConfigureAwait(false);
        var makers = (await routines.ListAsync(context.Routine.OfficeId, cancellationToken).ConfigureAwait(false)).Where(r => r.Kind == CvMaker.KindKey).ToList();
        foreach (var maker in makers)
        {
            requests.Ask(maker.Id);
        }

        return $"Noted as interesting and saved in {folder}" + (makers.Count > 0 ? ": its CV is on the way." : ". Add a CV routine to this office to have a CV made for it.");
    }

    public async Task RejectedAsync(Proposal proposal, RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);
        var payload = Payload(proposal);
        await context.MarkAsync(payload.OfferKey, SeenState.Dismissed, cancellationToken).ConfigureAwait(false);
        await context.MarkAsync(payload.RoleKey, SeenState.Dismissed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The person's own rules: in Japan, a job and not an internship, a role they look for, a visa when they need one, no more
    /// Japanese than they allow, not paid less than they ask. What is not known yet does not count
    /// against an offer: the local model reads it, and the rules are asked again.
    /// </summary>
    public static bool Passes(JobSettings settings, JobOffer offer)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(offer);
        return offer.InJapan
            && !Internship(offer.Title)
            && (settings.Roles.Count == 0 || settings.Roles.Any(r => offer.Title.Contains(r, StringComparison.OrdinalIgnoreCase)))
            && (!settings.Visa || (offer.SponsorsVisa != false && offer.FromAbroad != false))
            && offer.Japanese <= settings.JapaneseUpTo
            && (settings.MinSalary <= 0 || offer.SalaryMax is not { } top || top >= settings.MinSalary);
    }

    /// <summary>"acme-senior-net-developer": the offer's folder, readable and short.</summary>
    public static string FolderName(string company, string title)
    {
        var words = string.Join('-', $"{company} {title}".ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries));
        words = words.Length <= 80 ? words : words[..80].TrimEnd('-');
        return words.Length > 0 ? words : "offer";
    }

    /// <summary>"Internship - Research Engineer", "Software Engineer Intern": not a job to move for.</summary>
    private static bool Internship(string title) =>
        title.Split(Separators, StringSplitOptions.RemoveEmptyEntries).Any(w => w.Equals("intern", StringComparison.OrdinalIgnoreCase) || w.Equals("internship", StringComparison.OrdinalIgnoreCase))
        || title.Contains("インターン", StringComparison.Ordinal);

    private static JapaneseLevel Level(string? said) =>
        Enum.TryParse<JapaneseLevel>(said, ignoreCase: true, out var level) ? level : JapaneseLevel.Unknown;

    private static bool? Visa(string? said) => said?.Trim().ToUpperInvariant() switch
    {
        "YES" => true,
        "NO" => false,
        _ => null,
    };

    /// <summary>The link without what changes nothing: "https://board.example/jobs/acme/dev".</summary>
    public static string OfferKey(JobOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return OfferPrefix + $"{offer.Url.Host.ToLowerInvariant()}{offer.Url.AbsolutePath.TrimEnd('/')}";
    }

    /// <summary>"role:acme|senior-full-stack-developer": the same job whatever link it comes under.</summary>
    public static string RoleKey(JobOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        static string Words(string text) =>
            string.Join('-', text.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries));
        var key = $"{Words(offer.Company)}|{Words(offer.Title)}";
        return RolePrefix + (key.Length <= 200 ? key : key[..200]);
    }

    /// <summary>What makes an offer a different one when it changes: not the date it was posted again.</summary>
    private static string Fingerprint(JobOffer offer) =>
        RoutineContext.Fingerprint(offer.Title, offer.Location, offer.Japanese.ToString(), offer.SponsorsVisa?.ToString(), offer.SalaryMin?.ToString(CultureInfo.InvariantCulture), offer.SalaryMax?.ToString(CultureInfo.InvariantCulture));

    private static int CityRank(JobSettings settings, JobOffer offer)
    {
        var rank = settings.Cities.ToList().FindIndex(c => offer.Location.Contains(c, StringComparison.OrdinalIgnoreCase));
        return rank < 0 ? settings.Cities.Count : rank;
    }

    private async Task<Judgement?> JudgeAsync(
        RoutineContext context, JobSettings settings, string profile, JobOffer offer, CancellationToken cancellationToken, Decided? decided = null)
    {
        try
        {
            var brought = offer.Board == Brought;
            var details = offer.Text ?? await boards.First(b => b.Reads(offer.Board)).DetailsAsync(offer, cancellationToken).ConfigureAwait(false);
            var answer = await chat.AskAsync(context,
                new LocalAsk(
                    settings.Model,
                    $"""
                    You help one person find a job. Write in {settings.Language}.
                    Judge honestly from the profile and the offer only: never invent skills, experience or facts about the company.
                    The person's profile:
                    {Short(profile, 6000)}
                    {Wishes(settings, decided)}
                    """,
                    $"""
                    {(brought
                        ? "A job offer the person found and pasted: read its title, company and place from the text, as written."
                        : $"A job offer.\n{offer.Title} at {offer.Company}, {offer.Location}. Seniority: {offer.Seniority}. Remote: {offer.Remote}. Japanese: {offer.Japanese}.\nSalary: {Salary(offer, JobWords.For("English"))}. Kind: {string.Join(", ", offer.Tags)}.")}

                    {Short(details, Reading)}

                    How well does it fit the person, from 1 (not at all) to 5 (made for them)? Weigh the skills and experience it asks against theirs, what they look for, and whether they could grow into it; an offer like those they said no to, or mainly about what they do not want, fits 2 at most. Then, in one short sentence each, of twenty words at most: why it fits ("why"), and what they would have to fill in or watch out for ("gaps").
                    Then the role in detail, each point one short line, taken from the offer and the profile only:
                    - "role": what the person would do day to day, in one or two sentences;
                    - "have": the skills and experience the offer asks for that the person has;
                    - "missing": those it asks for that they lack or have only in part;
                    - "improve": for each missing one, a concrete way to fill it or show it (a course, a small project, what to say at the interview);
                    - "ask": what to clarify with the company (visa, remote work, Japanese, salary, seniority), only what the offer leaves open.
                    Write every text in {settings.Language}, whatever the language of the offer and of the notes above; names of technologies stay as they are.
                    Also say what the offer itself states, never guessed: how much Japanese it asks for ("none", "basic", "conversational", "business", "fluent", or "unknown" when it does not say), and whether the company sponsors a work visa ("yes", "no", or "unknown" when it does not say). Read the whole text for them: they are often in the description.
                    And the pay, when the text states it: in "salaryLow" and "salaryHigh" the numbers as written (one number: both the same; none: 0), in "salaryUnit" how they are written ("yen" for ¥8,000,000; "man-yen" for 800万円; "million-yen" for ¥8M or 8 million yen; "other" for another currency), and in "salaryPer" what they pay for ("year", "month", "hour", or "unknown").{(brought ? "\nAnd its title, company and place, as the text writes them." : string.Empty)}
                    """,
                    JudgementSchema, MaxTokens: 1500, Temperature: 0.2),
                cancellationToken).ConfigureAwait(false);
            var judgement = JsonSerializer.Deserialize<Judgement>(answer, JsonSerializerOptions.Web);
            return judgement is { Fit: >= 1 and <= 5 } && !string.IsNullOrWhiteSpace(judgement.Why)
                ? judgement with
                {
                    Why = Short(judgement.Why.Trim(), 220),
                    Gaps = Short((judgement.Gaps ?? string.Empty).Trim(), 220),
                    Role = Short((judgement.Role ?? string.Empty).Trim(), 400),
                    Have = Points(judgement.Have),
                    Missing = Points(judgement.Missing),
                    Improve = Points(judgement.Improve),
                    Ask = Points(judgement.Ask),
                    Details = details,
                }
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"{offer.Title} at {offer.Company} could not be judged ({ex.Message}).");
            return null;
        }
    }

    /// <summary>The heading before the offers: how many, and how to answer.</summary>
    internal static OutboxMessage Heading(JobSettings settings, JobWords words, int count) => new(
        $"💼 <b>{OutboxMessage.Escape(settings.Title)}</b> · {OutboxMessage.Escape(words.New(count))}\n{OutboxMessage.Escape(words.Decide)}",
        $"💼 {settings.Title} · {words.New(count)}\n{words.Decide}");

    /// <summary>
    /// One offer in a message of its own, in short lines with room between them: the role in bold,
    /// company and place; fit, salary, Japanese and visa; why it fits and what to watch; the link.
    /// </summary>
    internal static OutboxMessage Message(JobWords words, JobOffer offer, int fit, string why, string? gaps, RoleDetail? detail = null)
    {
        // Telegram takes 4096 characters: fewer points per list rather than a message cut short.
        foreach (var most in new[] { 6, 3, 1 })
        {
            var message = Compose(words, offer, fit, why, gaps, detail?.Fewer(most));
            if (message.Html.Length <= MessageRoom || most == 1)
            {
                return message;
            }
        }

        throw new UnreachableException();
    }

    private const int MessageRoom = 3900;

    private static OutboxMessage Compose(JobWords words, JobOffer offer, int fit, string why, string? gaps, RoleDetail? detail)
    {
        string E(string text) => OutboxMessage.Escape(text);
        var facts = $"{Stars(fit)} · {Salary(offer, words)}\n🇯🇵 {Capital(words.Japanese(offer.Japanese))} · 🛂 {Capital(offer.SponsorsVisa == true ? words.Visa : words.VisaToCheck)}";

        // With the role in detail, what to watch is in it; without, it stands under why it fits.
        var notes = $"👍 {why}" + (detail is { IsEmpty: false } || string.IsNullOrWhiteSpace(gaps) ? string.Empty : $"\n⚠️ {gaps}");
        var link = words.Open(offer.Url);
        var folded = detail is { IsEmpty: false } d ? $"\n\n<blockquote expandable><b>{E(words.InDetail)}</b>\n{E(d.Plain(words))}</blockquote>" : string.Empty;
        var open = detail is { IsEmpty: false } p ? $"\n\n{words.InDetail}\n{p.Plain(words)}" : string.Empty;
        return new OutboxMessage(
            $"<b>{E(offer.Title)}</b>\n{E(offer.Company)} · {E(offer.Location)}\n\n{E(facts)}\n\n{E(notes)}{folded}\n\n<a href=\"{E(offer.Url.ToString())}\">{E(link)}</a>",
            $"{offer.Title}\n{offer.Company} · {offer.Location}\n\n{facts}\n\n{notes}{open}\n\n{link}: {offer.Url}");
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>"★★★★☆": the fit at a glance.</summary>
    public static string Stars(int fit) => new string('★', Math.Clamp(fit, 0, 5)) + new string('☆', 5 - Math.Clamp(fit, 0, 5));

    /// <summary>"¥7–10M · Japanese not required · visa sponsored", or "visa to check" when nothing says.</summary>
    private static string Facts(JobOffer offer, JobWords words) =>
        $"{Salary(offer, words)} · {words.Japanese(offer.Japanese)} · {(offer.SponsorsVisa == true ? words.Visa : words.VisaToCheck)}";

    private static string Salary(JobOffer offer, JobWords words)
    {
        static string Millions(long yen) => (yen / 1_000_000m).ToString("0.##", CultureInfo.InvariantCulture);
        return (offer.SalaryMin, offer.SalaryMax) switch
        {
            ({ } min, { } max) when min != max => $"¥{Millions(min)}–{Millions(max)}M",
            ({ } one, _) => $"¥{Millions(one)}M",
            (_, { } one) => $"¥{Millions(one)}M",
            _ => words.NoSalary,
        };
    }

    private static OfferPayload Payload(Proposal proposal) =>
        JsonSerializer.Deserialize<OfferPayload>(proposal.Payload, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The proposal does not say which offer it is.");

    private static string Short(string text, int most) => text.Length <= most ? text : string.Concat(text.AsSpan(0, most - 1), "…");

    private static readonly char[] Separators = [.. Enumerable.Range(0, 128).Select(i => (char)i).Where(c => !char.IsLetterOrDigit(c))];

    /// <summary>Points of a list the model wrote: blank ones left out, each short, six at most.</summary>
    private static List<string> Points(IReadOnlyList<string>? points) =>
        [.. (points ?? []).Select(p => p?.Trim().TrimStart('-', '•', '*', ' ') ?? string.Empty).Where(p => p.Length > 0).Distinct().Take(6).Select(p => Short(p, 180))];

    private const string JudgementSchema = """
        {"type":"object","properties":{"fit":{"type":"integer","minimum":1,"maximum":5},"why":{"type":"string"},"gaps":{"type":"string"},
          "role":{"type":"string"},"have":{"type":"array","items":{"type":"string"}},"missing":{"type":"array","items":{"type":"string"}},
          "improve":{"type":"array","items":{"type":"string"}},"ask":{"type":"array","items":{"type":"string"}},
          "japanese":{"type":"string","enum":["none","basic","conversational","business","fluent","unknown"]},
          "visa":{"type":"string","enum":["yes","no","unknown"]},
          "salaryLow":{"type":"number"},"salaryHigh":{"type":"number"},
          "salaryUnit":{"type":"string","enum":["yen","man-yen","million-yen","other"]},
          "salaryPer":{"type":"string","enum":["year","month","hour","unknown"]},
          "title":{"type":"string"},"company":{"type":"string"},"location":{"type":"string"}},"required":["fit","why","gaps","role","have","missing","improve","ask","japanese","visa","salaryLow","salaryHigh","salaryUnit","salaryPer"]}
        """;

    /// <param name="Title">Read from the text, for offers the person brings; the same for <paramref name="Company"/> and <paramref name="Location"/>.</param>
    /// <param name="Details">The offer's text the model read, kept for its CV.</param>
    /// <param name="Role">What the person would do there; with <paramref name="Have"/>, <paramref name="Missing"/>, <paramref name="Improve"/> and <paramref name="Ask"/>, the role in detail.</param>
    private sealed record Judgement(
        int Fit, string Why, string? Gaps, string? Japanese = null, string? Visa = null, string? Title = null, string? Company = null, string? Location = null,
        string? Details = null, string? Role = null, IReadOnlyList<string>? Have = null, IReadOnlyList<string>? Missing = null, IReadOnlyList<string>? Improve = null,
        IReadOnlyList<string>? Ask = null, decimal? SalaryLow = null, decimal? SalaryHigh = null, string? SalaryUnit = null, string? SalaryPer = null)
    {
        public RoleDetail Detail => new(Role ?? string.Empty, Have ?? [], Missing ?? [], Improve ?? [], Ask ?? []);

        /// <summary>
        /// A pay the text states, as yen a year: 800万円 is 8,000,000, a month counts twelve; null
        /// for other currencies, pay by the hour, or a number that cannot be a yearly pay in Japan.
        /// </summary>
        public long? YearlyYen(decimal? amount)
        {
            if (amount is not > 0)
            {
                return null;
            }

            var yen = SalaryUnit?.Trim().ToLowerInvariant() switch
            {
                "yen" => amount.Value,
                "man-yen" => amount.Value * 10_000,
                "million-yen" => amount.Value * 1_000_000,
                _ => 0m,
            };
            yen *= SalaryPer?.Trim().ToLowerInvariant() switch
            {
                "month" => 12,
                "year" or "unknown" or null => 1,
                _ => 0,
            };
            return yen is >= 1_000_000 and <= 100_000_000 ? (long)yen : null;
        }
    }

    /// <param name="Fit">The role in detail as the judge wrote it, plain, for the offer's file and its CV.</param>
    private sealed record OfferPayload(
        string OfferKey, string RoleKey, string Url, string Title = "", string Company = "", string Location = "", string Facts = "", string Text = "", string Fit = "");
}

/// <summary>A job offers routine's settings, read from its JSON.</summary>
/// <param name="Profile">The file with who the person is and what they look for, which the model reads.</param>
/// <param name="Visa">Only offers that sponsor a visa and take people from abroad.</param>
/// <param name="MinSalary">Yearly, in yen: offers whose top is lower are left out; offers that do not say stay.</param>
/// <param name="Cities">Preferred cities, the dearest first: their offers come first.</param>
/// <param name="Most">New offers judged per run at most; the rest wait for the next.</param>
/// <param name="Fit">The least fit, from 1 to 5, worth showing.</param>
/// <param name="Boards">Where offers come from: "japan-dev", or a hiring service and company, "lever:acme".</param>
/// <param name="Roles">Words of which a title must have one: the roles looked for; none takes every role.</param>
/// <param name="Inbox">Where the offers the person brings wait to be judged, one file each.</param>
/// <param name="Offers">Where an offer the person wants goes, in a folder of its own, for its CV.</param>
/// <param name="Avoid">What the person does not want, in their words ("Go", "Kubernetes", "QA"): the desk and the judge keep it away.</param>
public sealed record JobSettings(
    string Title, string Language, string Model, string Profile, IReadOnlyList<JobSettings.BoardChoice> Boards, IReadOnlyList<string> Roles, bool Visa,
    JapaneseLevel JapaneseUpTo, long MinSalary, IReadOnlyList<string> Cities, int Most, int Fit,
    string Inbox = "~/Documents/CV/inbox", string Offers = "~/Documents/CV/offers", IReadOnlyList<string>? AvoidList = null)
{
    /// <summary>What the person does not want; none when the settings say nothing.</summary>
    public IReadOnlyList<string> Avoid => AvoidList ?? [];

    /// <param name="AllInJapan">Every job of the board is in Japan, whatever its place says ("Hybrid").</param>
    public sealed record BoardChoice(string Board, bool AllInJapan);

    private static readonly string[] DefaultRoles =
        ["developer", "engineer", "programmer", "frontend", "front-end", "backend", "back-end", "full stack", "full-stack", "fullstack", "software", "エンジニア"];

    public static JobSettings Read(JsonElement settings)
    {
        string Text(string name, string fallback) =>
            settings.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : fallback;
        List<string> List(string name) =>
            settings.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
                ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString())).Select(x => x.GetString()!.Trim())]
                : [];

        // A board is a name, "lever:acme", or an object saying so: { "board": "greenhouse:acme", "allInJapan": true }.
        var boards = settings.TryGetProperty("boards", out var b) && b.ValueKind == JsonValueKind.Array
            ? b.EnumerateArray()
                .Select(x => x.ValueKind switch
                {
                    JsonValueKind.String => new BoardChoice(x.GetString()!.Trim(), false),
                    JsonValueKind.Object when x.TryGetProperty("board", out var name) && name.ValueKind == JsonValueKind.String =>
                        new BoardChoice(name.GetString()!.Trim(), x.TryGetProperty("allInJapan", out var all) && all.ValueKind == JsonValueKind.True),
                    _ => null,
                })
                .OfType<BoardChoice>()
                .Where(c => c.Board.Length > 0)
                .DistinctBy(c => c.Board, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
        return new JobSettings(
            Text("title", "Job offers"), Text("language", "English"), Text("model", "local/gemma"), Text("profile", "~/Documents/CV/profile.md"),
            boards.Count > 0 ? boards : [new BoardChoice("japan-dev", false)],
            settings.TryGetProperty("roles", out var r) && r.ValueKind == JsonValueKind.Array ? List("roles") : DefaultRoles,
            !settings.TryGetProperty("visa", out var visa) || visa.ValueKind != JsonValueKind.False,
            Enum.TryParse<JapaneseLevel>(Text("japaneseUpTo", "Conversational"), ignoreCase: true, out var level) && level != JapaneseLevel.Unknown ? level : JapaneseLevel.Conversational,
            settings.TryGetProperty("minSalary", out var s) && s.TryGetInt64(out var salary) ? Math.Max(0, salary) : 0,
            List("cities"),
            settings.TryGetProperty("most", out var m) && m.TryGetInt32(out var most) ? Math.Clamp(most, 1, 40) : 8,
            settings.TryGetProperty("fit", out var f) && f.TryGetInt32(out var fit) ? Math.Clamp(fit, 1, 5) : 3,
            Text("inbox", "~/Documents/CV/inbox"),
            Text("offers", "~/Documents/CV/offers"),
            List("avoid"));
    }
}

/// <summary>An offer's role in detail: what the person would do, the skills asked they have and lack, how to fill the gaps, what to clarify.</summary>
public sealed record RoleDetail(string Role, IReadOnlyList<string> Have, IReadOnlyList<string> Missing, IReadOnlyList<string> Improve, IReadOnlyList<string> Ask)
{
    public bool IsEmpty => Role.Length == 0 && Have.Count == 0 && Missing.Count == 0 && Improve.Count == 0 && Ask.Count == 0;

    /// <summary>The same detail with at most <paramref name="most"/> points per list.</summary>
    public RoleDetail Fewer(int most) => new(Role, [.. Have.Take(most)], [.. Missing.Take(most)], [.. Improve.Take(most)], [.. Ask.Take(most)]);

    /// <summary>The detail as short lines under their headings, each part only when it says something.</summary>
    internal string Plain(JobWords words)
    {
        var parts = new List<string>();
        if (Role.Length > 0)
        {
            parts.Add($"📋 {words.Role}: {Role}");
        }

        void List(string heading, IReadOnlyList<string> points)
        {
            if (points.Count > 0)
            {
                parts.Add($"{heading}\n{string.Join('\n', points.Select(p => $"• {p}"))}");
            }
        }

        List($"✅ {words.Have}", Have);
        List($"🧩 {words.Missing}", Missing);
        List($"📈 {words.Improve}", Improve);
        List($"❓ {words.AskAbout}", Ask);
        return string.Join("\n\n", parts);
    }
}

/// <summary>The message's few fixed words, in the person's language.</summary>
internal sealed record JobWords(
    Func<int, string> New, Func<JapaneseLevel, string> Japanese, string Visa, string NoSalary, string Gaps, string Ask, string Decide, string VisaToCheck, string Untitled,
    string OpenOffer = "Open the offer", string On = "on", string InDetail = "The role in detail", string Role = "What you would do", string Have = "You already have",
    string Missing = "You lack", string Improve = "How to fill the gap", string AskAbout = "To clarify", string Yes = "✅ I'm interested", string No = "❌ No")
{
    /// <summary>"Apri l'offerta su LinkedIn": the link's words, naming the board when it is a known one.</summary>
    public string Open(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var host = url.Host.ToLowerInvariant();
        var board = host switch
        {
            _ when host.EndsWith("linkedin.com", StringComparison.Ordinal) => "LinkedIn",
            _ when host.EndsWith("japan-dev.com", StringComparison.Ordinal) => "Japan Dev",
            _ when host.EndsWith("tokyodev.com", StringComparison.Ordinal) => "TokyoDev",
            _ when host.EndsWith("daijob.com", StringComparison.Ordinal) => "Daijob",
            _ => null,
        };
        return board is null ? OpenOffer : $"{OpenOffer} {On} {board}";
    }

    public static JobWords For(string language) => language.Trim().ToUpperInvariant() is "ITALIAN" or "ITALIANO" ? Italian : English;

    private static readonly JobWords Italian = new(
        n => n == 1 ? "1 offerta nuova per te" : $"{n} offerte nuove per te",
        level => level switch
        {
            JapaneseLevel.None => "giapponese non richiesto",
            JapaneseLevel.Basic => "giapponese di base",
            JapaneseLevel.Conversational => "giapponese di conversazione",
            JapaneseLevel.Business => "giapponese da lavoro",
            JapaneseLevel.Fluent or JapaneseLevel.Native => "giapponese fluente",
            _ => "giapponese non indicato",
        },
        "visto sponsorizzato",
        "stipendio non indicato",
        "Da colmare",
        "Ti interessa? Sì la segna tra quelle da seguire, no la scarta per sempre.",
        "Rispondi con i bottoni sotto ogni offerta: «mi interessa» e arriva il CV, «no» e non torna più.",
        "visto da verificare",
        "Offerta senza titolo",
        "Apri l'offerta",
        "su",
        "Il ruolo nel dettaglio",
        "Cosa faresti",
        "Hai già",
        "Ti manca",
        "Come colmarlo",
        "Da chiarire",
        "✅ Mi interessa",
        "❌ No");

    private static readonly JobWords English = new(
        n => n == 1 ? "1 new offer for you" : $"{n} new offers for you",
        level => level switch
        {
            JapaneseLevel.None => "Japanese not required",
            JapaneseLevel.Basic => "basic Japanese",
            JapaneseLevel.Conversational => "conversational Japanese",
            JapaneseLevel.Business => "business Japanese",
            JapaneseLevel.Fluent or JapaneseLevel.Native => "fluent Japanese",
            _ => "Japanese not stated",
        },
        "visa sponsored",
        "salary not stated",
        "To fill in",
        "Interested? Yes keeps it among those to follow, no drops it for good.",
        "Answer with the buttons under each offer: interested and its CV follows, no and it never comes back.",
        "visa to check",
        "Untitled offer");
}
