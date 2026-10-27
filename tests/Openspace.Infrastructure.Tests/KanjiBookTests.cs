using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Options;
using Openspace.Infrastructure.Routines;

namespace Openspace.Infrastructure.Tests;

/// <summary>The kanji book on small dictionaries shaped like the real ones.</summary>
public sealed class KanjiBookTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"openspace-kanji-{Guid.NewGuid():N}");

    public KanjiBookTests()
    {
        Directory.CreateDirectory(_folder);
        Gzip(KanjiBook.Kanjidic, """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE kanjidic2 [
            <!ELEMENT kanjidic2 (character*)>
            ]>
            <kanjidic2>
            <character><literal>明</literal><misc><grade>2</grade><stroke_count>8</stroke_count><freq>67</freq></misc>
              <reading_meaning><rmgroup><reading r_type="pinyin">ming2</reading><reading r_type="ja_on">メイ</reading><reading r_type="ja_kun">あか.るい</reading>
              <meaning>bright</meaning><meaning m_lang="fr">clair</meaning></rmgroup></reading_meaning></character>
            <character><literal>日</literal><misc><grade>1</grade><stroke_count>4</stroke_count><freq>1</freq></misc>
              <reading_meaning><rmgroup><reading r_type="ja_on">ニチ</reading><reading r_type="ja_kun">ひ</reading><meaning>day</meaning><meaning>sun</meaning></rmgroup></reading_meaning></character>
            <character><literal>月</literal><misc><grade>1</grade><stroke_count>4</stroke_count><freq>23</freq></misc>
              <reading_meaning><rmgroup><reading r_type="ja_on">ゲツ</reading><meaning>month</meaning></rmgroup></reading_meaning></character>
            <character><literal>丶</literal><misc><stroke_count>1</stroke_count></misc></character>
            </kanjidic2>
            """);
        Gzip(KanjiBook.KanjiVg, """
            <?xml version="1.0" encoding="UTF-8"?>
            <kanjivg xmlns:kvg="http://kanjivg.tagaini.net">
            <kanji id="kvg:kanji_065e5"><g id="kvg:065e5" kvg:element="日">
              <path id="kvg:065e5-s1" d="M31.5,24.5c1,1,1.7,2.7,1.7,4.7"/><path id="kvg:065e5-s2" d="M33.4,26c0.8,0,37-3,40-3"/></g></kanji>
            <kanji id="kvg:kanji_0660e"><g id="kvg:0660e" kvg:element="明">
              <g id="kvg:0660e-g1" kvg:element="日"><path id="kvg:0660e-s1" d="M10,20L12,60"/></g>
              <g id="kvg:0660e-g2" kvg:element="月"><path id="kvg:0660e-s2" d="M50,15L52,90"/></g></g></kanji>
            </kanjivg>
            """);
        Gzip(KanjiBook.JMdict, """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE JMdict [
            <!ELEMENT JMdict (entry*)>
            <!ENTITY n "noun (common) (futsuumeishi)">
            ]>
            <JMdict>
            <entry><k_ele><keb>明日</keb><ke_pri>ichi1</ke_pri></k_ele><r_ele><reb>あした</reb></r_ele><sense><pos>&n;</pos><gloss>tomorrow</gloss></sense></entry>
            <entry><k_ele><keb>説明</keb><ke_pri>nf02</ke_pri></k_ele><r_ele><reb>せつめい</reb></r_ele><sense><pos>&n;</pos><gloss>explanation</gloss></sense></entry>
            <entry><k_ele><keb>明かり</keb></k_ele><r_ele><reb>あかり</reb></r_ele><sense><gloss>light</gloss></sense></entry>
            <entry><k_ele><keb>明明白白</keb><ke_pri>nf40</ke_pri></k_ele><r_ele><reb>めいめいはくはく</reb></r_ele><sense><gloss>crystal clear</gloss></sense></entry>
            <entry><k_ele><keb>明後日日日</keb><ke_pri>spec1</ke_pri></k_ele><k_ele><keb>明るい日の日</keb><ke_pri>spec1</ke_pri></k_ele><r_ele><reb>あさって</reb></r_ele><sense><gloss>too long</gloss></sense></entry>
            </JMdict>
            """);
        File.WriteAllText(Path.Combine(_folder, KanjiBook.Transcriptions), string.Join('\n',
            "10\tjpn\tHrkt\tsomeone\t[明日|あした]は[晴|は]れるかな。",
            "11\tjpn\tHrkt\t\t[明|あか]るい[部屋|へ|や]が[好|す]きです。",
            "12\tjpn\tHrkt\tsomeone\t[説明|せつ|めい]してください。",
            "13\tjpn\tHrkt\tsomeone\t[月|つき]がきれいですね。"));
        File.WriteAllText(Path.Combine(_folder, KanjiBook.ItalianLinks), "11\t900\n");
        File.WriteAllText(Path.Combine(_folder, KanjiBook.ItalianSentences), "900\tita\tMi piacciono le stanze luminose.\n");
        File.WriteAllText(Path.Combine(_folder, KanjiBook.EnglishLinks), "10\t800\n12\t801\n13\t802\n");
        File.WriteAllText(Path.Combine(_folder, KanjiBook.EnglishSentences), "800\teng\tWill it be sunny tomorrow?\n801\teng\tPlease explain.\n802\teng\tThe moon is lovely.\n");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private KanjiBook Book(string? folder = null) => new(new FixedOptions(new KanjiOptions { Folder = folder ?? _folder }));

    private void Gzip(string name, string xml)
    {
        using var file = File.Create(Path.Combine(_folder, name));
        using var zip = new GZipStream(file, CompressionLevel.Fastest);
        zip.Write(Encoding.UTF8.GetBytes(xml.TrimStart()));
    }

    [Fact]
    public async Task The_curriculum_goes_by_school_year_the_most_common_first_and_only_with_common_use_kanji()
    {
        using var book = Book();

        var curriculum = await book.CurriculumAsync(default);

        Assert.Equal(["日", "月", "明"], curriculum.Select(k => k.Literal));
        Assert.Equal([1, 1, 2], curriculum.Select(k => k.Grade));
    }

    [Fact]
    public async Task A_kanji_comes_with_readings_strokes_parts_common_words_and_translated_sentences()
    {
        using var book = Book();

        var entry = await book.LookUpAsync("明", default);

        Assert.NotNull(entry);
        Assert.Equal(["bright"], entry.Meanings);
        Assert.Equal(["メイ"], entry.On);
        Assert.Equal(["あか(るい)"], entry.Kun);
        Assert.Equal(["M10,20L12,60", "M50,15L52,90"], entry.Strokes);
        Assert.Equal(["日", "月"], entry.Parts);

        // The basic vocabulary first, then the most common; none without a frequency mark, none longer than four characters.
        Assert.Equal(["明日", "説明", "明明白白"], entry.Words.Select(w => w.Written));
        Assert.Equal("あした", entry.Words[0].Reading);

        // A sentence people translated into Italian comes first, even when no one checked its reading.
        Assert.Equal(2, entry.Sentences.Count);
        Assert.Equal("明るい部屋が好きです。", entry.Sentences[0].Text);
        Assert.Equal("Mi piacciono le stanze luminose.", entry.Sentences[0].Italian);
        Assert.Equal("[明|あか]るい[部屋|へ|や]が[好|す]きです。", entry.Sentences[0].Furigana);
        Assert.Equal("Will it be sunny tomorrow?", entry.Sentences[1].English);
    }

    [Fact]
    public async Task A_kanji_built_from_nothing_else_has_no_parts()
    {
        using var book = Book();

        var entry = await book.LookUpAsync("日", default);

        Assert.NotNull(entry);
        Assert.Empty(entry.Parts);
        Assert.Equal(2, entry.Strokes.Count);
        Assert.Null(await book.LookUpAsync("丶", default));
    }

    [Fact]
    public void Without_its_dictionaries_the_book_is_not_available()
    {
        using var book = Book(Path.Combine(_folder, "missing"));

        Assert.False(book.Available);
        Assert.True(Book().Available);
    }

    [Fact]
    public void Readings_are_taken_out_of_a_sentence_to_search_it() =>
        Assert.Equal("場所に行く", KanjiBook.Plain("[場所|ば|しょ]に[行|い]く"));

    private sealed class FixedOptions(KanjiOptions value) : IOptionsMonitor<KanjiOptions>
    {
        public KanjiOptions CurrentValue => value;

        public KanjiOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<KanjiOptions, string?> listener) => null;
    }
}
