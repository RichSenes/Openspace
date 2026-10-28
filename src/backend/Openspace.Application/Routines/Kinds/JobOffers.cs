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
/// posted again under another.
/// </summary>
public sealed class JobOffers(IEnumerable<IJobBoard> boards, ILocalChat chat) : IRoutineKind
{
    public const string InterestedAction = "interested";

    /// <summary>Offers by their link.</summary>
    public const string OfferPrefix = "job:";

    /// <summary>Offers by company and title, so the same job posted again under another link is known.</summary>
    public const string RolePrefix = "role:";

    public string Key => "job-offers";

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
          "fit": 3
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
        var offers = new List<JobOffer>();
        foreach (var choice in settings.Boards)
        {
            if (boards.FirstOrDefault(b => b.Reads(choice.Board)) is not { } board)
            {
                context.Write($"No reader for the board {choice.Board}: write it as \"japan-dev\" or as service and company, \"lever:acme\".");
                continue;
            }

            context.Progress($"Reading {choice.Board}");
            try
            {
                var found = await board.LatestAsync(choice.Board, cancellationToken).ConfigureAwait(false);
                offers.AddRange(choice.AllInJapan ? found.Select(o => o with { InJapan = true }) : found);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                context.Write($"{choice.Board} could not be read ({ex.Message}).");
            }
        }

        var kept = offers.Where(o => Passes(settings, o)).ToList();
        var fresh = new List<JobOffer>();
        foreach (var offer in kept.OrderByDescending(o => o.Posted))
        {
            if (fresh.Count < settings.Most && !fresh.Any(f => RoleKey(f) == RoleKey(offer))
                && await context.IsNewAsync(OfferKey(offer), Fingerprint(offer), cancellationToken).ConfigureAwait(false)
                && await context.IsNewAsync(RoleKey(offer), Fingerprint(offer), cancellationToken).ConfigureAwait(false))
            {
                fresh.Add(offer);
            }
        }

        context.Write($"{offers.Count} offers read, {kept.Count} fit your rules, {fresh.Count} new to judge.");

        var good = new List<(JobOffer Offer, Judgement Judgement)>();
        var judged = 0;
        foreach (var (listed, i) in fresh.Select((o, i) => (o, i)))
        {
            context.Progress($"Measuring offer {i + 1} of {fresh.Count} against your profile");
            if (await JudgeAsync(context, settings, profile, listed, cancellationToken).ConfigureAwait(false) is not { } judgement)
            {
                continue;
            }

            judged++;

            // What the board did not say, the text may: the rules hold for it too.
            var offer = listed with
            {
                Japanese = listed.Japanese == JapaneseLevel.Unknown ? Level(judgement.Japanese) : listed.Japanese,
                SponsorsVisa = listed.SponsorsVisa ?? Visa(judgement.Visa),
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

        if (fresh.Count > 0 && judged == 0)
        {
            throw new InvalidOperationException("The local model could not judge any offer: they stay new for the next run.");
        }

        // The preferred cities first, in their order; then the better fit.
        good = [.. good.OrderBy(g => CityRank(settings, g.Offer)).ThenByDescending(g => g.Judgement.Fit)];
        foreach (var (offer, judgement) in good)
        {
            await context.ProposeAsync(
                InterestedAction,
                Short($"{offer.Title} — {offer.Company}, {offer.Location}", 200),
                Short($"{Facts(offer, words)} · {judgement.Fit}/5\n\n{judgement.Why}\n\n{words.Gaps}: {judgement.Gaps}\n\n{offer.Url}\n\n{words.Ask}", 20_000),
                new OfferPayload(OfferKey(offer), RoleKey(offer), offer.Url.ToString()),
                cancellationToken).ConfigureAwait(false);
        }

        if (good.Count == 0)
        {
            return $"No new offers for you: {kept.Count} of {offers.Count} fit your rules, {judged} new ones judged.";
        }

        await context.DeliverAsync(Summary(settings, words, good), null, cancellationToken).ConfigureAwait(false);
        return $"{good.Count} new offers for you, of {judged} judged; {kept.Count} of {offers.Count} fit your rules.";
    }

    public async Task<string> CarryOutAsync(Proposal proposal, RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);
        var payload = Payload(proposal);
        await context.MarkAsync(payload.OfferKey, SeenState.Taken, cancellationToken).ConfigureAwait(false);
        await context.MarkAsync(payload.RoleKey, SeenState.Taken, cancellationToken).ConfigureAwait(false);
        return $"Noted as interesting: {payload.Url}";
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

    private async Task<Judgement?> JudgeAsync(RoutineContext context, JobSettings settings, string profile, JobOffer offer, CancellationToken cancellationToken)
    {
        try
        {
            var board = boards.First(b => b.Reads(offer.Board));
            var details = await board.DetailsAsync(offer, cancellationToken).ConfigureAwait(false);
            var answer = await chat.AskAsync(context,
                new LocalAsk(
                    settings.Model,
                    $"""
                    You help one person find a job. Write in {settings.Language}.
                    Judge honestly from the profile and the offer only: never invent skills, experience or facts about the company.
                    The person's profile:
                    {Short(profile, 6000)}
                    """,
                    $"""
                    A job offer.
                    {offer.Title} at {offer.Company}, {offer.Location}. Seniority: {offer.Seniority}. Remote: {offer.Remote}. Japanese: {offer.Japanese}.
                    Salary: {Salary(offer, JobWords.For("English"))}. Kind: {string.Join(", ", offer.Tags)}.

                    {Short(details, Reading)}

                    How well does it fit the person, from 1 (not at all) to 5 (made for them)? Weigh the skills and experience it asks against theirs, what they look for, and whether they could grow into it. Then, in one or two short sentences each: why it fits, and what they would have to fill in or watch out for.
                    Also say what the offer itself states, never guessed: how much Japanese it asks for ("none", "basic", "conversational", "business", "fluent", or "unknown" when it does not say), and whether the company sponsors a work visa ("yes", "no", or "unknown" when it does not say).
                    """,
                    JudgementSchema, MaxTokens: 600, Temperature: 0.2),
                cancellationToken).ConfigureAwait(false);
            var judgement = JsonSerializer.Deserialize<Judgement>(answer, JsonSerializerOptions.Web);
            return judgement is { Fit: >= 1 and <= 5 } && !string.IsNullOrWhiteSpace(judgement.Why)
                ? judgement with { Why = Short(judgement.Why.Trim(), 600), Gaps = Short((judgement.Gaps ?? string.Empty).Trim(), 600) }
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"{offer.Title} at {offer.Company} could not be judged ({ex.Message}).");
            return null;
        }
    }

    private static string Summary(JobSettings settings, JobWords words, List<(JobOffer Offer, Judgement Judgement)> good)
    {
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"{settings.Title}: {words.New(good.Count)}");
        foreach (var ((offer, judgement), i) in good.Select((g, i) => (g, i)))
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{i + 1}. {offer.Title} — {offer.Company} ({offer.Location})");
            text.Append(CultureInfo.InvariantCulture, $"\n{Facts(offer, words)} · {judgement.Fit}/5");
            text.Append(CultureInfo.InvariantCulture, $"\n{judgement.Why}");
            if (!string.IsNullOrWhiteSpace(judgement.Gaps))
            {
                text.Append(CultureInfo.InvariantCulture, $"\n{words.Gaps}: {judgement.Gaps}");
            }

            text.Append(CultureInfo.InvariantCulture, $"\n{offer.Url}");
        }

        text.Append(CultureInfo.InvariantCulture, $"\n\n{words.Decide}");
        return text.ToString();
    }

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

    private const string JudgementSchema = """
        {"type":"object","properties":{"fit":{"type":"integer","minimum":1,"maximum":5},"why":{"type":"string"},"gaps":{"type":"string"},
          "japanese":{"type":"string","enum":["none","basic","conversational","business","fluent","unknown"]},
          "visa":{"type":"string","enum":["yes","no","unknown"]}},"required":["fit","why","gaps","japanese","visa"]}
        """;

    private sealed record Judgement(int Fit, string Why, string? Gaps, string? Japanese = null, string? Visa = null);

    private sealed record OfferPayload(string OfferKey, string RoleKey, string Url);
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
public sealed record JobSettings(
    string Title, string Language, string Model, string Profile, IReadOnlyList<JobSettings.BoardChoice> Boards, IReadOnlyList<string> Roles, bool Visa,
    JapaneseLevel JapaneseUpTo, long MinSalary, IReadOnlyList<string> Cities, int Most, int Fit)
{
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
            settings.TryGetProperty("fit", out var f) && f.TryGetInt32(out var fit) ? Math.Clamp(fit, 1, 5) : 3);
    }
}

/// <summary>The message's few fixed words, in the person's language.</summary>
internal sealed record JobWords(Func<int, string> New, Func<JapaneseLevel, string> Japanese, string Visa, string NoSalary, string Gaps, string Ask, string Decide, string VisaToCheck)
{
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
        "In Openspace, per ognuna: «sì» se ti interessa, «no» e non torna più.",
        "visto da verificare");

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
        "In Openspace, for each: yes if it interests you, no and it never comes back.",
        "visa to check");
}
