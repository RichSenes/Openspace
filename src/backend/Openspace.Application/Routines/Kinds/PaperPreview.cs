using System.Text;
using System.Text.Json;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// The paper as its settings lay it out, filled with sample text and grey frames for the
/// pictures: no feed is read and no model is asked, so it is ready at once and shows only the
/// layout. Every section prints as many articles as it may, as long as its settings ask.
/// </summary>
public static class PaperPreview
{
    private const string Sentence =
        "This is sample text, set where the day's story will go, so the page shows how much room each part takes.";

    public static string Render(string settings, DateOnly day)
    {
        using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(settings) ? "{}" : settings);
        var paper = PaperSettings.Read(json.RootElement);
        var culture = MorningPaper.CultureOf(paper.Language);

        var lead = new PaperLead(
            paper.Sections[0].Name,
            new PaperArticle("The day's opening story, with the headline set large", Text(6), []),
            [],
            Frame());
        var good = new PaperLead(paper.Sections[0].Name, new PaperArticle("The good news of the day", Text(3), []), [], null);
        var opening = new PaperOpening("Good morning", Text(3));
        var sections = paper.Sections.Select(section => new PaperSection(
            section.Name,
            [.. Enumerable.Range(1, paper.ArticlesOf(section)).Select(n => new PaperArticle($"{section.Name}: headline of story {n}", Text(SentencesIn(section)), []))],
            [],
            Frame())).ToList();
        return PaperPage.Render(paper, culture, 1, day, opening, lead, good, sections);
    }

    /// <summary>Sentences of a sample article: the most its length allows, to show the room it may take.</summary>
    private static int SentencesIn(PaperSettings.Section section) => PaperSettings.LengthOf(section) switch
    {
        "short" => 2,
        "long" => 7,
        _ => 4,
    };

    private static string Text(int sentences) => string.Join(' ', Enumerable.Repeat(Sentence, sentences));

    /// <summary>A grey frame where a picture will go.</summary>
    private static PaperPicture Frame()
    {
        const string svg = """<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="900" viewBox="0 0 1600 900"><rect width="1600" height="900" fill="#d8cfbf"/><path d="M0 0L1600 900M1600 0L0 900" stroke="#b9ad99" stroke-width="4"/></svg>""";
        return new PaperPicture(new WebImage(Encoding.UTF8.GetBytes(svg), "image/svg+xml", 1600, 900), "Picture");
    }
}
