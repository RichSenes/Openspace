using System.Globalization;
using System.Text.Json;
using Openspace.Application.LocalModels;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// The person's CVs, made from their profile by a local model and printed to PDF. When the
/// profile, the personal data or the photo change, a new version of the whole set goes in a
/// folder of its own, numbered: a CV in each language, and for Japan the rirekisho and the
/// shokumu keirekisho. Each offer the person said yes to, saved in a folder of its own, gets
/// a CV made for it: only what the role needs. The model writes only what the profile says.
/// </summary>
public sealed class CvMaker(ILocalChat chat, IPdfPrinter printer) : IRoutineKind
{
    public const string KindKey = "cv";

    /// <summary>The set made last, by what it was made from.</summary>
    public const string MasterKey = "cv:master";

    /// <summary>Raised when the pages change, so a new look makes a new version.</summary>
    private const string Design = "1";

    /// <summary>Offers given a CV per run at most.</summary>
    private const int MostOffers = 5;

    /// <summary>Of an offer, the start the model reads.</summary>
    private const int Reading = 6000;

    public string Key => KindKey;

    public string Name => "CV";

    public string Description =>
        "Makes your CVs from your profile when it changes (each language, and the Japanese rirekisho and shokumu keirekisho) and a CV for each offer you want.";

    public string DefaultSettings => """
        {
          "title": "CV",
          "channel": "",
          "model": "local/gemma",
          "folder": "~/Documents/CV",
          "profile": "~/Documents/CV/profile.md",
          "personal": "~/Documents/CV/personal.json",
          "offers": "~/Documents/CV/offers",
          "languages": ["English", "Italian", "Japanese"],
          "japaneseForms": true,
          "tailoredLanguage": "English"
        }
        """;

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = CvSettings.Read(context.Settings);
        var profilePath = MorningPaper.Expand(settings.Profile);
        if (!File.Exists(profilePath))
        {
            throw new InvalidOperationException($"No profile at {settings.Profile}: write who you are there, or change \"profile\".");
        }

        var personalPath = MorningPaper.Expand(settings.Personal);
        if (!File.Exists(personalPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(personalPath)!);
            await File.WriteAllTextAsync(personalPath, PersonalData.Template, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Fill in your name and contacts in {settings.Personal}, just made for you, then run again.");
        }

        var profile = await File.ReadAllTextAsync(profilePath, cancellationToken).ConfigureAwait(false);
        var personalText = await File.ReadAllTextAsync(personalPath, cancellationToken).ConfigureAwait(false);
        var personal = PersonalData.Read(personalText);
        var photo = await PhotoAsync(context, personalPath, personal, cancellationToken).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.Now, context.Clock.LocalTimeZone).DateTime);
        var tailored = await TailorAsync(context, settings, profile, personal, photo, cancellationToken).ConfigureAwait(false);

        var fingerprint = RoutineContext.Fingerprint(profile, personalText, photo?.Fingerprint, Design, string.Join(',', settings.Languages), settings.JapaneseForms.ToString());
        string? version = null;
        if (await context.IsNewAsync(MasterKey, fingerprint, cancellationToken).ConfigureAwait(false))
        {
            version = await MasterAsync(context, settings, profile, personal, photo, today, cancellationToken).ConfigureAwait(false);
            await context.MeetAsync(MasterKey, fingerprint, $"CV {Path.GetFileName(version)}", cancellationToken).ConfigureAwait(false);
            await context.MarkAsync(MasterKey, SeenState.Shown, cancellationToken).ConfigureAwait(false);
        }

        return (version, tailored.Count) switch
        {
            (null, 0) => "Nothing new: the profile has not changed and no offer waits for a CV.",
            (null, var n) => $"{n} CVs made for offers.",
            (var v, 0) => $"New set in {v}.",
            (var v, var n) => $"New set in {v}; {n} CVs made for offers.",
        };
    }

    /// <summary>A CV for each offer folder without one yet, in the offer's own words where they are true of the person.</summary>
    private async Task<List<string>> TailorAsync(
        RoutineContext context, CvSettings settings, string profile, PersonalData personal, Photo? photo, CancellationToken cancellationToken)
    {
        var offers = MorningPaper.Expand(settings.Offers);
        var waiting = Directory.Exists(offers)
            ? Directory.GetDirectories(offers)
                .Where(d => File.Exists(Path.Combine(d, JobOffers.OfferFile)) && Directory.GetFiles(d, "CV_*.pdf").Length == 0)
                .Order(StringComparer.Ordinal)
                .Take(MostOffers)
                .ToList()
            : [];
        var made = new List<string>();
        foreach (var (folder, i) in waiting.Select((f, i) => (f, i)))
        {
            context.Progress($"Writing the CV for offer {i + 1} of {waiting.Count}");
            var offer = await File.ReadAllTextAsync(Path.Combine(folder, JobOffers.OfferFile), cancellationToken).ConfigureAwait(false);
            var content = await AskAsync<CvContent>(context, settings, settings.TailoredLanguage,
                $"""
                A CV for this job offer, in {settings.TailoredLanguage}, to fit on two pages.
                Choose from the profile what the role needs and put it first; leave out what does not help; use the offer's words for skills the person truly has.
                Never add a skill, a tool, a title or a result the profile does not state. In "focus", say in one sentence what the CV puts forward.

                The offer:
                {Short(offer, Reading)}
                """,
                profile, cancellationToken).ConfigureAwait(false);
            var labels = CvLabels.For(settings.TailoredLanguage);
            var file = Path.Combine(folder, $"CV_{personal.FileName}.pdf");
            await PrintAsync(CvPages.Cv(content, personal, photo?.DataUri, labels), file, cancellationToken).ConfigureAwait(false);
            await context.DeliverAsync($"{settings.Title}: {Path.GetFileName(folder)}" + (content.Focus is { Length: > 0 } focus ? $"\n{focus}" : string.Empty), file, cancellationToken)
                .ConfigureAwait(false);
            made.Add(file);
        }

        return made;
    }

    /// <summary>The whole set, in a new numbered folder: "v3-2026-11-12".</summary>
    private async Task<string> MasterAsync(
        RoutineContext context, CvSettings settings, string profile, PersonalData personal, Photo? photo, DateOnly today, CancellationToken cancellationToken)
    {
        var pages = new List<(string Name, string Html)>();
        foreach (var language in settings.Languages)
        {
            context.Progress($"Writing the CV in {language}");
            var content = await AskAsync<CvContent>(context, settings, language,
                $"A complete CV in {language}, to fit on two pages: the most recent and strongest first. Leave \"focus\" empty.",
                profile, cancellationToken).ConfigureAwait(false);
            pages.Add(($"CV_{personal.FileName}_{CvLabels.For(language).Code}.pdf", CvPages.Cv(content, personal, photo?.DataUri, CvLabels.For(language))));
        }

        if (settings.JapaneseForms)
        {
            context.Progress("Writing the rirekisho");
            var form = await AskAsync<Rirekisho>(context, settings, "Japanese",
                """
                The rirekisho (履歴書), in Japanese, as Japanese companies expect it.
                "education" and "work" are its 学歴 and 職歴 rows, oldest first: year and month as numbers, and the text in the usual style ("…大学 …学部 卒業", "…株式会社 入社", the current job ending with "現在に至る"). Only dates the profile states: a row whose month is unknown gets 0.
                "licenses": its 免許・資格 rows. "motivation": 志望の動機・特技・アピールポイント, a few sentences in polite Japanese. "wishes": 本人希望記入欄, short: what the person needs (a work visa sponsored), then 「その他は貴社の規定に従います。」
                """,
                profile, cancellationToken).ConfigureAwait(false);
            pages.Add(($"履歴書_{personal.FileName}.pdf", CvPages.Rirekisho(form, personal, photo?.DataUri, today)));

            context.Progress("Writing the shokumu keirekisho");
            var history = await AskAsync<Shokumu>(context, settings, "Japanese",
                """
                The shokumu keirekisho (職務経歴書), in Japanese, as Japanese companies expect it.
                "summary": 職務要約, three or four sentences. "jobs": each job with its company, period, what the company does, the employment ("正社員" and the like) and the role, and its work as short points. "projects": personal projects the same way.
                "skills": 活かせる経験・知識・技術 by group. "pr": 自己PR, a short paragraph in polite Japanese.
                """,
                profile, cancellationToken).ConfigureAwait(false);
            pages.Add(($"職務経歴書_{personal.FileName}.pdf", CvPages.Shokumu(history, personal, today)));
        }

        var root = MorningPaper.Expand(settings.Folder);
        Directory.CreateDirectory(root);
        var number = Directory.GetDirectories(root).Select(Path.GetFileName).Count(n => n is ['v', >= '0' and <= '9', ..]) + 1;
        var folder = Path.Combine(root, $"v{number}-{today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        Directory.CreateDirectory(folder);
        var files = new List<string>();
        foreach (var (name, html) in pages)
        {
            context.Progress($"Printing {name}");
            var file = Path.Combine(folder, name);
            await PrintAsync(html, file, cancellationToken).ConfigureAwait(false);
            files.Add(file);
        }

        await context.DeliverAsync($"{settings.Title}: v{number}, {files.Count} files in {folder}", null, cancellationToken).ConfigureAwait(false);
        foreach (var file in files)
        {
            await context.DeliverAsync(Path.GetFileName(file), file, cancellationToken).ConfigureAwait(false);
        }

        return folder;
    }

    private async Task PrintAsync(string html, string file, CancellationToken cancellationToken)
    {
        var pdf = await printer.PrintAsync(html, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(file, pdf, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> AskAsync<T>(RoutineContext context, CvSettings settings, string language, string task, string profile, CancellationToken cancellationToken)
    {
        var answer = await chat.AskAsync(context,
            new LocalAsk(
                settings.Model,
                $"""
                You write a person's CV from their profile. Write in {language}.
                Use only what the profile says: never invent experience, skills, dates, titles, numbers or places. Keep names of companies, products and technologies as they are.
                Leave out phone numbers and addresses: they are added apart.
                The profile:
                {Short(profile, 12_000)}
                """,
                task,
                Schema<T>(), MaxTokens: 5000, Temperature: 0.3),
            cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(answer, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The local model wrote no CV.");
    }

    private static string Schema<T>() => typeof(T) == typeof(CvContent) ? CvContent.Schema : typeof(T) == typeof(Rirekisho) ? Rirekisho.Schema : Shokumu.Schema;

    /// <summary>The person's photo, next to their personal data, as the pages embed it.</summary>
    private static async Task<Photo?> PhotoAsync(RoutineContext context, string personalPath, PersonalData personal, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(personal.Photo))
        {
            return null;
        }

        var path = Path.Combine(Path.GetDirectoryName(personalPath)!, personal.Photo);
        var type = Path.GetExtension(path).ToUpperInvariant() switch
        {
            ".JPG" or ".JPEG" => "image/jpeg",
            ".PNG" => "image/png",
            ".WEBP" => "image/webp",
            _ => null,
        };
        if (type is null || !File.Exists(path) || new FileInfo(path).Length > 8 * 1024 * 1024)
        {
            context.Write($"The photo {personal.Photo} is not there, or is not a JPEG, PNG or WebP under 8 MB: the pages go without it.");
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return new Photo($"data:{type};base64,{Convert.ToBase64String(bytes)}", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16]);
    }

    private static string Short(string text, int most) => text.Length <= most ? text : string.Concat(text.AsSpan(0, most - 1), "…");

    private sealed record Photo(string DataUri, string Fingerprint);
}

/// <summary>A CV routine's settings, read from its JSON.</summary>
/// <param name="Folder">Where each new set goes, in a numbered folder.</param>
/// <param name="Personal">The JSON file with name, contacts and photo: kept on this computer, never in the profile the model reads.</param>
/// <param name="Offers">The offers' folders, each with its offer; the job offers routine saves them there.</param>
/// <param name="JapaneseForms">Also the rirekisho and the shokumu keirekisho.</param>
public sealed record CvSettings(
    string Title, string Model, string Folder, string Profile, string Personal, string Offers, IReadOnlyList<string> Languages, bool JapaneseForms, string TailoredLanguage)
{
    public static CvSettings Read(JsonElement settings)
    {
        string Text(string name, string fallback) =>
            settings.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : fallback;

        var languages = settings.TryGetProperty("languages", out var l) && l.ValueKind == JsonValueKind.Array
            ? l.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString())).Select(x => x.GetString()!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : ["English"];
        return new CvSettings(
            Text("title", "CV"), Text("model", "local/gemma"), Text("folder", "~/Documents/CV"), Text("profile", "~/Documents/CV/profile.md"),
            Text("personal", "~/Documents/CV/personal.json"), Text("offers", "~/Documents/CV/offers"), languages,
            !settings.TryGetProperty("japaneseForms", out var j) || j.ValueKind != JsonValueKind.False,
            Text("tailoredLanguage", "English"));
    }
}

/// <summary>
/// What a CV says about the person that the model must not write: name, contacts, photo; and
/// for the Japanese forms, the name in katakana, the birth date and the address.
/// </summary>
public sealed record PersonalData(
    string Name, string NameKana, string BirthDate, string Address, string AddressKana, string Email, string Phone, string Location,
    IReadOnlyList<string> Links, string Photo)
{
    public const string Template = """
        {
          "name": "",
          "nameKana": "",
          "birthDate": "",
          "address": "",
          "addressKana": "",
          "email": "",
          "phone": "",
          "location": "",
          "links": [],
          "photo": ""
        }
        """;

    /// <summary>"Ada-Lovelace", for file names.</summary>
    public string FileName => string.Join('-', Name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) is { Length: > 0 } name ? name : "CV";

    public DateOnly? Birth => DateOnly.TryParseExact(BirthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    public static PersonalData Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : string.Empty;
        var name = Text("name");
        if (name.Length == 0)
        {
            throw new InvalidOperationException("The personal data names no one: write your name in \"name\".");
        }

        return new PersonalData(
            name, Text("nameKana"), Text("birthDate"), Text("address"), Text("addressKana"), Text("email"), Text("phone"), Text("location"),
            root.TryGetProperty("links", out var l) && l.ValueKind == JsonValueKind.Array
                ? [.. l.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString())).Select(x => x.GetString()!.Trim())]
                : [],
            Text("photo"));
    }
}

/// <summary>A CV's content, as the model writes it in one language.</summary>
public sealed record CvContent(
    string Headline, string Summary, IReadOnlyList<CvContent.Job> Experience, IReadOnlyList<CvContent.Project> Projects, IReadOnlyList<CvContent.Study> Education,
    IReadOnlyList<CvContent.SkillGroup> Skills, IReadOnlyList<CvContent.Language> Languages, IReadOnlyList<CvContent.Certificate> Certifications, string Interests,
    string? Focus = null)
{
    public sealed record Job(string Role, string Company, string Place, string Period, IReadOnlyList<string> Points);

    public sealed record Project(string Name, string Kind, string Period, string Link, IReadOnlyList<string> Points);

    public sealed record Study(string Title, string School, string Period, string Note);

    public sealed record SkillGroup(string Group, IReadOnlyList<string> Items);

    public sealed record Language(string Name, string Level);

    public sealed record Certificate(string Name, string Issuer, string Year);

    internal const string Schema = """
        {"type":"object","properties":{
          "headline":{"type":"string"},"summary":{"type":"string"},
          "experience":{"type":"array","items":{"type":"object","properties":{"role":{"type":"string"},"company":{"type":"string"},"place":{"type":"string"},"period":{"type":"string"},
            "points":{"type":"array","items":{"type":"string"}}},"required":["role","company","place","period","points"]}},
          "projects":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"kind":{"type":"string"},"period":{"type":"string"},"link":{"type":"string"},
            "points":{"type":"array","items":{"type":"string"}}},"required":["name","kind","period","link","points"]}},
          "education":{"type":"array","items":{"type":"object","properties":{"title":{"type":"string"},"school":{"type":"string"},"period":{"type":"string"},"note":{"type":"string"}},
            "required":["title","school","period","note"]}},
          "skills":{"type":"array","items":{"type":"object","properties":{"group":{"type":"string"},"items":{"type":"array","items":{"type":"string"}}},"required":["group","items"]}},
          "languages":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"level":{"type":"string"}},"required":["name","level"]}},
          "certifications":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"issuer":{"type":"string"},"year":{"type":"string"}},"required":["name","issuer","year"]}},
          "interests":{"type":"string"},"focus":{"type":"string"}},
         "required":["headline","summary","experience","projects","education","skills","languages","certifications","interests","focus"]}
        """;
}

/// <summary>The rirekisho's rows and boxes, as the model writes them in Japanese.</summary>
public sealed record Rirekisho(IReadOnlyList<Rirekisho.Row> Education, IReadOnlyList<Rirekisho.Row> Work, IReadOnlyList<Rirekisho.Row> Licenses, string Motivation, string Wishes)
{
    /// <param name="Month">0 when the profile does not say.</param>
    public sealed record Row(int Year, int Month, string Text);

    internal const string Schema = """
        {"type":"object","properties":{
          "education":{"type":"array","items":{"type":"object","properties":{"year":{"type":"integer"},"month":{"type":"integer"},"text":{"type":"string"}},"required":["year","month","text"]}},
          "work":{"type":"array","items":{"type":"object","properties":{"year":{"type":"integer"},"month":{"type":"integer"},"text":{"type":"string"}},"required":["year","month","text"]}},
          "licenses":{"type":"array","items":{"type":"object","properties":{"year":{"type":"integer"},"month":{"type":"integer"},"text":{"type":"string"}},"required":["year","month","text"]}},
          "motivation":{"type":"string"},"wishes":{"type":"string"}},
         "required":["education","work","licenses","motivation","wishes"]}
        """;
}

/// <summary>The shokumu keirekisho's parts, as the model writes them in Japanese.</summary>
public sealed record Shokumu(
    string Summary, IReadOnlyList<Shokumu.Job> Jobs, IReadOnlyList<CvContent.Project> Projects, IReadOnlyList<CvContent.SkillGroup> Skills, string Pr)
{
    public sealed record Job(string Company, string Period, string Business, string Employment, string Role, IReadOnlyList<string> Points);

    internal const string Schema = """
        {"type":"object","properties":{
          "summary":{"type":"string"},
          "jobs":{"type":"array","items":{"type":"object","properties":{"company":{"type":"string"},"period":{"type":"string"},"business":{"type":"string"},"employment":{"type":"string"},
            "role":{"type":"string"},"points":{"type":"array","items":{"type":"string"}}},"required":["company","period","business","employment","role","points"]}},
          "projects":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"kind":{"type":"string"},"period":{"type":"string"},"link":{"type":"string"},
            "points":{"type":"array","items":{"type":"string"}}},"required":["name","kind","period","link","points"]}},
          "skills":{"type":"array","items":{"type":"object","properties":{"group":{"type":"string"},"items":{"type":"array","items":{"type":"string"}}},"required":["group","items"]}},
          "pr":{"type":"string"}},
         "required":["summary","jobs","projects","skills","pr"]}
        """;
}

/// <summary>A CV's headings in one language, and the code its file names carry.</summary>
public sealed record CvLabels(
    string Code, string Lang, string Profile, string Experience, string Projects, string Education, string Skills, string Languages, string Certifications, string Interests)
{
    public static CvLabels For(string language) => language.Trim().ToUpperInvariant() switch
    {
        "ITALIAN" or "ITALIANO" => new("it", "it", "Profilo", "Esperienza", "Progetti", "Formazione", "Competenze", "Lingue", "Certificazioni", "Interessi"),
        "JAPANESE" or "日本語" => new("ja", "ja", "プロフィール", "職歴", "プロジェクト", "学歴", "スキル", "語学", "資格", "興味・関心"),
        _ => new("en", "en", "Profile", "Experience", "Projects", "Education", "Skills", "Languages", "Certifications", "Interests"),
    };
}
