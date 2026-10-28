namespace Openspace.Application.Routines;

/// <summary>How much Japanese a job asks for, from none to native.</summary>
public enum JapaneseLevel
{
    Unknown,
    None,
    Basic,
    Conversational,
    Business,
    Fluent,
    Native,
}

/// <summary>A job offer as a board lists it.</summary>
/// <param name="Board">The board it comes from, as settings name it: "japan-dev", "lever:acme".</param>
/// <param name="Url">The offer's own page.</param>
/// <param name="Japanese">What the board says; unknown when it does not, and the local model reads it from the text.</param>
/// <param name="SponsorsVisa">Whether the company sponsors a work visa; null when the board does not say.</param>
/// <param name="FromAbroad">Whether people living outside Japan may apply; null when the board does not say.</param>
/// <param name="SalaryMin">Yearly, in yen; null when not stated.</param>
/// <param name="Tags">The board's own words for the kind of job: "Web / Full-stack", "Backend".</param>
/// <param name="InJapan">The job is in Japan, as far as its place says.</param>
/// <param name="Text">The full text, when the list already gives it; otherwise the board reads it on request.</param>
public sealed record JobOffer(
    string Board, Uri Url, string Company, string Title, string Location, JapaneseLevel Japanese, bool? SponsorsVisa, bool? FromAbroad,
    long? SalaryMin, long? SalaryMax, string Seniority, string Remote, IReadOnlyList<string> Tags, DateTimeOffset? Posted,
    bool InJapan = true, string? Text = null);

/// <summary>Job boards routines read, honestly identified, through what they publish for anyone to read.</summary>
public interface IJobBoard
{
    /// <summary>Whether it reads this board, as settings name it: "japan-dev", "greenhouse:acme".</summary>
    bool Reads(string board);

    /// <summary>The board's newest offers, as it lists them.</summary>
    Task<IReadOnlyList<JobOffer>> LatestAsync(string board, CancellationToken cancellationToken);

    /// <summary>The offer's full text, plain, shortened.</summary>
    Task<string> DetailsAsync(JobOffer offer, CancellationToken cancellationToken);
}

/// <summary>Places in Japan, as job boards write them: in English or in Japanese.</summary>
public static class JapanPlaces
{
    private static readonly string[] Names =
    [
        "japan", "tokyo", "osaka", "kyoto", "yokohama", "nagoya", "fukuoka", "sapporo", "kobe", "sendai", "hiroshima", "kawasaki", "saitama",
        "chiba", "kanagawa", "aichi", "hyogo", "okinawa", "日本", "東京", "大阪", "京都", "横浜", "名古屋", "福岡", "札幌", "神戸", "仙台", "広島",
    ];

    public static bool Mentions(string place) => Names.Any(n => place.Contains(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A title written mostly in Japanese is for people who read it: "AD/ADAS SIL環境開発エンジニア" is,
    /// "Backend Engineer / バックエンドエンジニア" is not.
    /// </summary>
    public static bool WrittenInJapanese(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var japanese = title.Count(c => c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿');
        return japanese > 0 && japanese >= title.Count(char.IsAsciiLetter);
    }
}
