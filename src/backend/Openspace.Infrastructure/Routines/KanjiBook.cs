using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Routines;

public sealed class KanjiOptions
{
    public const string SectionName = "Kanji";

    /// <summary>Where scripts/openspace kanji-data puts the dictionaries.</summary>
    public string Folder { get; set; } = "~/.local/share/openspace/kanji";
}

/// <summary>
/// The kanji of the common use list, read from open dictionaries kept on this computer:
/// KANJIDIC2 for readings and meanings, KanjiVG for the strokes, JMdict for the words and
/// Tatoeba for sentences people wrote and translated. Nothing is fetched while reading.
/// </summary>
internal sealed partial class KanjiBook(IOptionsMonitor<KanjiOptions> options) : IKanjiBook, IDisposable
{
    public const string Kanjidic = "kanjidic2.xml.gz";
    public const string KanjiVg = "kanjivg.xml.gz";
    public const string JMdict = "JMdict_e.gz";
    public const string Transcriptions = "jpn_transcriptions.tsv";
    public const string ItalianLinks = "jpn-ita_links.tsv";
    public const string EnglishLinks = "jpn-eng_links.tsv";
    public const string ItalianSentences = "ita_sentences.tsv";
    public const string EnglishSentences = "eng_sentences.tsv";

    private const int WordsPerKanji = 4;
    private const int SentencesPerKanji = 2;

    private readonly SemaphoreSlim _loading = new(1, 1);
    private (string Folder, DateTime Written, Dictionary<string, Character> Characters)? _kanjidic;

    public void Dispose() => _loading.Dispose();

    public string Folder => Expand(options.CurrentValue.Folder);

    public bool Available => File.Exists(Path.Combine(Folder, Kanjidic)) && File.Exists(Path.Combine(Folder, KanjiVg)) && File.Exists(Path.Combine(Folder, JMdict));

    public async Task<IReadOnlyList<KanjiInCourse>> CurriculumAsync(CancellationToken cancellationToken) =>
        [.. (await CharactersAsync(cancellationToken).ConfigureAwait(false)).Values
            .OrderBy(c => c.Grade).ThenBy(c => c.Frequency ?? int.MaxValue).ThenBy(c => c.Literal, StringComparer.Ordinal)
            .Select(c => new KanjiInCourse(c.Literal, c.Grade, c.Meanings))];

    public async Task<KanjiEntry?> LookUpAsync(string literal, CancellationToken cancellationToken)
    {
        var characters = await CharactersAsync(cancellationToken).ConfigureAwait(false);
        if (!characters.TryGetValue(literal, out var character))
        {
            return null;
        }

        var folder = Folder;
        return await Task.Run(
            () =>
            {
                var (strokes, parts) = ReadStrokes(Path.Combine(folder, KanjiVg), literal);
                return new KanjiEntry(
                    literal, character.Grade, character.StrokeCount, character.Meanings, character.On, character.Kun, strokes, parts,
                    ReadWords(Path.Combine(folder, JMdict), literal, cancellationToken),
                    ReadSentences(folder, literal, characters, cancellationToken));
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, Character>> CharactersAsync(CancellationToken cancellationToken)
    {
        var folder = Folder;
        var path = Path.Combine(folder, Kanjidic);
        var written = File.GetLastWriteTimeUtc(path);
        if (_kanjidic is { } kept && kept.Folder == folder && kept.Written == written)
        {
            return kept.Characters;
        }

        await _loading.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var characters = await Task.Run(() => ReadKanjidic(path), cancellationToken).ConfigureAwait(false);
            _kanjidic = (folder, written, characters);
            return characters;
        }
        finally
        {
            _loading.Release();
        }
    }

    /// <summary>The kanji taught in school (grades 1 to 6) and the rest of the common use list (grade 8).</summary>
    internal static Dictionary<string, Character> ReadKanjidic(string path)
    {
        var characters = new Dictionary<string, Character>(StringComparer.Ordinal);
        using var reader = Open(path);
        while (reader.ReadToFollowing("character"))
        {
            var element = (XElement)XNode.ReadFrom(reader);
            var misc = element.Element("misc");
            if (!int.TryParse(misc?.Element("grade")?.Value, CultureInfo.InvariantCulture, out var grade) || grade is < 1 or > 8)
            {
                continue;
            }

            var literal = element.Element("literal")?.Value ?? string.Empty;
            var group = element.Element("reading_meaning")?.Element("rmgroup");
            var readings = group?.Elements("reading").ToList() ?? [];
            characters[literal] = new Character(
                literal, grade,
                int.TryParse(misc!.Element("stroke_count")?.Value, CultureInfo.InvariantCulture, out var strokes) ? strokes : 0,
                int.TryParse(misc.Element("freq")?.Value, CultureInfo.InvariantCulture, out var frequency) ? frequency : null,
                [.. group?.Elements("meaning").Where(m => m.Attribute("m_lang") is null && !m.Value.StartsWith('(') && !m.Value.Contains("radical (no.", StringComparison.Ordinal)).Select(m => m.Value) ?? []],
                [.. readings.Where(r => (string?)r.Attribute("r_type") == "ja_on").Select(r => r.Value)],
                [.. readings.Where(r => (string?)r.Attribute("r_type") == "ja_kun").Select(r => Kun(r.Value))]);
        }

        return characters;
    }

    /// <summary>"ひか.る" as "ひか(る)": the part after the dot is written in kana after the kanji.</summary>
    private static string Kun(string reading) =>
        reading.IndexOf('.', StringComparison.Ordinal) is var dot and >= 0 ? $"{reading[..dot]}({reading[(dot + 1)..]})" : reading;

    /// <summary>The strokes in writing order, and the parts when the kanji is made of two or more.</summary>
    internal static (IReadOnlyList<string> Strokes, IReadOnlyList<string> Parts) ReadStrokes(string path, string literal)
    {
        var id = $"kvg:kanji_{char.ConvertToUtf32(literal, 0):x5}";
        using var reader = Open(path);
        while (reader.ReadToFollowing("kanji"))
        {
            if (reader.GetAttribute("id") != id)
            {
                continue;
            }

            var kanji = (XElement)XNode.ReadFrom(reader);
            XNamespace kvg = "http://kanjivg.tagaini.net";
            var strokes = kanji.Descendants("path").Select(p => (string?)p.Attribute("d") ?? string.Empty).Where(d => d.Length > 0).ToList();
            var whole = kanji.Element("g");
            var parts = whole?.Elements("g")
                .Select(g => (string?)g.Attribute(kvg + "element") ?? (string?)g.Attribute("element"))
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? [];
            return (strokes, parts.Count >= 2 ? parts : []);
        }

        return ([], []);
    }

    /// <summary>
    /// Common words written with the kanji, short ones, with their reading and first meanings:
    /// those of the basic vocabulary list first, then by how common, and never one that only
    /// adds to a word already chosen (労働者 after 労働).
    /// </summary>
    internal static IReadOnlyList<KanjiWord> ReadWords(string path, string literal, CancellationToken cancellationToken)
    {
        var found = new List<(KanjiWord Word, bool Basic, int Rank)>();
        using var reader = Open(path);
        while (reader.ReadToFollowing("entry"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = (XElement)XNode.ReadFrom(reader);
            foreach (var spelling in entry.Elements("k_ele"))
            {
                var written = spelling.Element("keb")?.Value ?? string.Empty;
                if (!written.Contains(literal, StringComparison.Ordinal) || written.Length > 4)
                {
                    continue;
                }

                var priorities = spelling.Elements("ke_pri").Select(p => p.Value).ToList();
                var rank = priorities.Select(Rank).DefaultIfEmpty(int.MaxValue).Min();
                var reading = entry.Elements("r_ele")
                    .Where(r => r.Element("re_nokanji") is null && (!r.Elements("re_restr").Any() || r.Elements("re_restr").Any(x => x.Value == written)))
                    .Select(r => r.Element("reb")?.Value)
                    .FirstOrDefault();
                var meanings = entry.Elements("sense").FirstOrDefault()?.Elements("gloss").Select(g => g.Value).Take(3).ToList() ?? [];
                if (rank < int.MaxValue && reading is not null && meanings.Count > 0)
                {
                    found.Add((new KanjiWord(written, reading, meanings), priorities.Contains("ichi1"), rank));
                }
            }
        }

        var chosen = new List<KanjiWord>();
        foreach (var (word, _, _) in found.OrderBy(f => f.Basic ? 0 : 1).ThenBy(f => f.Rank).ThenBy(f => f.Word.Written.Length))
        {
            if (chosen.Count == WordsPerKanji)
            {
                break;
            }

            if (!chosen.Exists(c => word.Written.Contains(c.Written, StringComparison.Ordinal) && c.Written != literal
                                    || c.Written == word.Written))
            {
                chosen.Add(word);
            }
        }

        return chosen;
    }

    /// <summary>How common a word is, lower first: "nf05" is in the fifth block of 500 most used in the press.</summary>
    private static int Rank(string priority) => priority switch
    {
        _ when priority.StartsWith("nf", StringComparison.Ordinal) && int.TryParse(priority[2..], CultureInfo.InvariantCulture, out var block) => block,
        "news1" or "ichi1" or "spec1" or "gai1" => 24,
        "news2" or "ichi2" or "spec2" or "gai2" => 40,
        _ => int.MaxValue,
    };

    /// <summary>
    /// Short sentences with the kanji and a known reading, with a translation people wrote:
    /// Italian ones first, then those whose other kanji come no later in school, then the shortest.
    /// </summary>
    internal static IReadOnlyList<KanjiSentence> ReadSentences(
        string folder, string literal, IReadOnlyDictionary<string, Character> characters, CancellationToken cancellationToken)
    {
        var transcriptions = Path.Combine(folder, Transcriptions);
        if (!File.Exists(transcriptions))
        {
            return [];
        }

        var grade = characters[literal].Grade;
        var candidates = new Dictionary<string, (string Text, string Furigana, bool Checked)>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(transcriptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = line.Split('\t');
            if (fields.Length < 5 || fields[1] != "jpn" || !fields[4].Contains(literal, StringComparison.Ordinal))
            {
                continue;
            }

            var text = Plain(fields[4]);
            if (text.Length is >= 7 and <= 26 && text.Contains(literal, StringComparison.Ordinal))
            {
                candidates.TryAdd(fields[0], (text, fields[4], fields[3].Length > 0));
            }
        }

        var italian = Translations(folder, ItalianLinks, ItalianSentences, candidates.Keys);
        var english = Translations(folder, EnglishLinks, EnglishSentences, candidates.Keys);
        return
        [
            .. candidates
                .Select(c => (Id: c.Key, c.Value, Italian: italian.GetValueOrDefault(c.Key), English: english.GetValueOrDefault(c.Key)))
                .Where(c => c.Italian is not null || c.English is not null)
                .OrderBy(c => c.Italian is null)
                .ThenBy(c => c.Value.Checked ? 0 : 1)
                .ThenBy(c => Harder(c.Value.Text, grade, characters))
                .ThenBy(c => c.Value.Text.Length)
                .ThenBy(c => c.Id, StringComparer.Ordinal)
                .DistinctBy(c => c.Value.Text)
                .Take(SentencesPerKanji)
                .Select(c => new KanjiSentence(c.Value.Text, c.Value.Furigana, c.Italian, c.English)),
        ];
    }

    /// <summary>How many of the sentence's kanji are taught later than <paramref name="grade"/>, or not in the list at all.</summary>
    private static int Harder(string text, int grade, IReadOnlyDictionary<string, Character> characters) =>
        text.Count(c => c is >= '一' and <= '鿿' && (!characters.TryGetValue(c.ToString(), out var other) || other.Grade > grade));

    /// <summary>The first translation of each sentence, from Tatoeba's links and sentences files.</summary>
    private static Dictionary<string, string> Translations(string folder, string links, string sentences, IEnumerable<string> ids)
    {
        var wanted = ids.ToHashSet(StringComparer.Ordinal);
        var linksPath = Path.Combine(folder, links);
        var sentencesPath = Path.Combine(folder, sentences);
        if (wanted.Count == 0 || !File.Exists(linksPath) || !File.Exists(sentencesPath))
        {
            return [];
        }

        var linked = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(linksPath))
        {
            var fields = line.Split('\t');
            if (fields.Length >= 2 && wanted.Contains(fields[0]))
            {
                linked.TryAdd(fields[1], fields[0]);
            }
        }

        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(sentencesPath))
        {
            var fields = line.Split('\t');
            if (fields.Length >= 3 && linked.TryGetValue(fields[0], out var japanese))
            {
                found.TryAdd(japanese, fields[2]);
            }
        }

        return found;
    }

    /// <summary>The sentence without its readings: "[今日|きょう]は" as "今日は".</summary>
    internal static string Plain(string furigana) => RubyMarkup().Replace(furigana, m => m.Groups[1].Value);

    [GeneratedRegex(@"\[([^\[\]|]+)(?:\|[^\[\]|]*)+\]")]
    private static partial Regex RubyMarkup();

    /// <summary>
    /// A gzipped XML file with its DTD, whose entities (JMdict's "&amp;n;") are expanded: JMdict
    /// expands to about 15 million characters in all, the limit stops a file that expands without end.
    /// </summary>
    private static XmlReader Open(string path)
    {
        var stream = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        return XmlReader.Create(
            new StreamReader(stream, Encoding.UTF8),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null, MaxCharactersFromEntities = 100_000_000, CloseInput = true, IgnoreComments = true });
    }

    private static string Expand(string folder) =>
        folder.StartsWith('~') ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder.TrimStart('~', '/')) : folder;

    internal sealed record Character(
        string Literal, int Grade, int StrokeCount, int? Frequency, IReadOnlyList<string> Meanings, IReadOnlyList<string> On, IReadOnlyList<string> Kun);
}
