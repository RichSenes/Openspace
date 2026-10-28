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
/// <param name="Url">The offer's own page on the board.</param>
/// <param name="SponsorsVisa">Whether the company sponsors a work visa; null when the board does not say.</param>
/// <param name="FromAbroad">Whether people living outside Japan may apply; null when the board does not say.</param>
/// <param name="SalaryMin">Yearly, in yen; null when not stated.</param>
/// <param name="Tags">The board's own words for the kind of job: "Web / Full-stack", "Backend".</param>
public sealed record JobOffer(
    string Board, Uri Url, string Company, string Title, string Location, JapaneseLevel Japanese, bool? SponsorsVisa, bool? FromAbroad,
    long? SalaryMin, long? SalaryMax, string Seniority, string Remote, IReadOnlyList<string> Tags, DateTimeOffset? Posted);

/// <summary>A job board routines read, honestly identified, through pages it serves to anyone.</summary>
public interface IJobBoard
{
    /// <summary>How settings name it: "japan-dev".</summary>
    string Key { get; }

    string Name { get; }

    /// <summary>The newest offers, as the board lists them.</summary>
    Task<IReadOnlyList<JobOffer>> LatestAsync(CancellationToken cancellationToken);

    /// <summary>The offer's full text, plain, shortened.</summary>
    Task<string> DetailsAsync(JobOffer offer, CancellationToken cancellationToken);
}
