using Openspace.Application.Abstractions;
using Openspace.Domain.Common;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines;

/// <param name="Grade">The school year it is taught in Japan, 1 to 6, or 8 for the rest of the common use list.</param>
/// <param name="Meanings">In English, as the dictionary gives them.</param>
/// <param name="On">The readings from Chinese, in katakana.</param>
/// <param name="Kun">The Japanese readings, in hiragana; the part written in kana after the kanji in brackets, "ひか(る)".</param>
/// <param name="Strokes">The strokes in the order they are written, each as SVG path data on a 109 by 109 grid.</param>
/// <param name="Parts">The kanji or shapes it is built from, when it is built from others.</param>
public sealed record KanjiEntry(
    string Literal, int Grade, int StrokeCount, IReadOnlyList<string> Meanings, IReadOnlyList<string> On, IReadOnlyList<string> Kun,
    IReadOnlyList<string> Strokes, IReadOnlyList<string> Parts, IReadOnlyList<KanjiWord> Words, IReadOnlyList<KanjiSentence> Sentences);

/// <param name="Meanings">In English, as the dictionary gives them.</param>
public sealed record KanjiWord(string Written, string Reading, IReadOnlyList<string> Meanings);

/// <param name="Furigana">The sentence with the reading of its kanji: "[今日|きょう]は[晴|は]れ".</param>
/// <param name="Italian">A translation people wrote, when there is one; the same for <paramref name="English"/>.</param>
public sealed record KanjiSentence(string Text, string Furigana, string? Italian, string? English);

/// <summary>A kanji in the course, with what it means in English.</summary>
public sealed record KanjiInCourse(string Literal, int Grade, IReadOnlyList<string> Meanings);

/// <summary>The kanji of the common use list, with what a learner needs: readings, strokes, words, sentences.</summary>
public interface IKanjiBook
{
    /// <summary>Whether its dictionaries are on this computer.</summary>
    bool Available { get; }

    /// <summary>Where they are expected, to tell the person.</summary>
    string Folder { get; }

    /// <summary>The kanji in the order to learn them: by school year, the most common first.</summary>
    Task<IReadOnlyList<KanjiInCourse>> CurriculumAsync(CancellationToken cancellationToken);

    Task<KanjiEntry?> LookUpAsync(string literal, CancellationToken cancellationToken);
}

/// <summary>
/// The kanji a routine explains, one a day in the curriculum's order. A kanji explained is
/// met as "kanji:日" and shown; one the person already knows is dismissed: neither comes back.
/// </summary>
public sealed class KanjiCourse(IKanjiBook book, IRoutineRepository routines, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public const string KeyPrefix = "kanji:";

    public static string KeyOf(string literal) => KeyPrefix + literal;

    /// <summary>The first kanji of the curriculum, from <paramref name="fromGrade"/> on, that is neither explained nor known.</summary>
    /// <param name="met">Keys of the kanji explained or known, as <see cref="RoutineContext.DealtWithAsync"/> gives them.</param>
    public static KanjiInCourse? Next(IReadOnlyList<KanjiInCourse> curriculum, IReadOnlySet<string> met, int fromGrade) =>
        curriculum.FirstOrDefault(k => k.Grade >= fromGrade && !met.Contains(KeyOf(k.Literal)));

    public async Task<KanjiCourseView> ViewAsync(RoutineId id, CancellationToken cancellationToken)
    {
        var routine = await routines.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (!book.Available)
        {
            return new KanjiCourseView(false, book.Folder, null, 0, 0);
        }

        var met = await routines.ListSeenAsync(routine.Id, KeyPrefix, cancellationToken).ConfigureAwait(false);
        var next = Next(
            await book.CurriculumAsync(cancellationToken).ConfigureAwait(false),
            met.Where(s => s.State != SeenState.Skipped).Select(s => s.Key).ToHashSet(StringComparer.Ordinal),
            KanjiSettings.Read(routine.Settings).FromGrade);
        return new KanjiCourseView(
            true, book.Folder, next is null ? null : new KanjiNextView(next.Literal, next.Grade, next.Meanings), met.Count(s => s.State == SeenState.Shown), met.Count(s => s.State == SeenState.Dismissed));
    }

    /// <summary>The person already knows it: it is never explained.</summary>
    public async Task KnownAsync(RoutineId id, string literal, CancellationToken cancellationToken)
    {
        var routine = await routines.GetAsync(id, cancellationToken).ConfigureAwait(false);
        var kanji = literal?.Trim() ?? string.Empty;
        if (kanji.Length is 0 or > 2)
        {
            throw new DomainException("Say which kanji, one at a time.");
        }

        var key = KeyOf(kanji);
        var now = clock.GetUtcNow();
        if (await routines.FindSeenAsync(routine.Id, key, cancellationToken).ConfigureAwait(false) is { } seen)
        {
            seen.Mark(SeenState.Dismissed, now);
        }
        else
        {
            routines.AddSeen(SeenItem.Meet(routine.Id, key, kanji, kanji, SeenState.Dismissed, now));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <param name="Explained">How many kanji the paper explained; <paramref name="Known"/>, how many the person said they know.</param>
public sealed record KanjiCourseView(bool Available, string Folder, KanjiNextView? Next, int Explained, int Known);

public sealed record KanjiNextView(string Literal, int Grade, IReadOnlyList<string> Meanings);

/// <summary>The kanji of the day in a routine's settings: "kanji": { "enabled": true, "fromGrade": 1 }.</summary>
public sealed record KanjiSettings(bool Enabled, int FromGrade)
{
    public static KanjiSettings Read(string settings)
    {
        using var document = System.Text.Json.JsonDocument.Parse(settings);
        return Read(document.RootElement);
    }

    public static KanjiSettings Read(System.Text.Json.JsonElement settings)
    {
        if (settings.ValueKind != System.Text.Json.JsonValueKind.Object
            || !settings.TryGetProperty("kanji", out var kanji) || kanji.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return new KanjiSettings(false, 1);
        }

        var enabled = kanji.TryGetProperty("enabled", out var on) && on.ValueKind == System.Text.Json.JsonValueKind.True;
        var from = kanji.TryGetProperty("fromGrade", out var grade) && grade.TryGetInt32(out var g) && g is >= 1 and <= 8 ? g : 1;
        return new KanjiSettings(enabled, from);
    }
}
