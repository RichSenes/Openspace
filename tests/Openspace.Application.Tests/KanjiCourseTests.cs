using Openspace.Application.Routines;
using Openspace.Domain.Common;
using Openspace.Domain.Routines;

namespace Openspace.Application.Tests;

public sealed class KanjiCourseTests
{
    private readonly Harness _h = new();
    private readonly FakeKanjiBook _book = new();

    private KanjiCourse Course() => new(_book, _h.Routines, _h.UnitOfWork, _h.Clock);

    private Routine Create(string kanji = """{"enabled":true}""")
    {
        var routine = Routine.Create(
            _h.Office, "Paper", "morning-paper", RoutineSchedule.Daily(new TimeOnly(6, 0)), $$"""{"kanji":{{kanji}}}""", _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.Add(routine);
        return routine;
    }

    [Fact]
    public async Task The_next_kanji_is_the_first_of_the_curriculum_not_yet_explained_or_known()
    {
        var routine = Create();
        _h.Routines.AddSeen(SeenItem.Meet(routine.Id, "kanji:日", "日", "日", SeenState.Shown, _h.Clock.GetUtcNow()));
        _h.Routines.AddSeen(SeenItem.Meet(routine.Id, "kanji:月", "月", "月", SeenState.Skipped, _h.Clock.GetUtcNow()));

        var view = await Course().ViewAsync(routine.Id, default);

        Assert.True(view.Available);
        Assert.Equal(("月", 1, 1, 0), (view.Next!.Literal, view.Next.Grade, view.Explained, view.Known));
        Assert.Equal(["month", "moon"], view.Next.Meanings);
    }

    [Fact]
    public async Task A_kanji_the_person_knows_is_skipped_for_good()
    {
        var routine = Create();
        var course = Course();

        await course.KnownAsync(routine.Id, "日", default);
        await course.KnownAsync(routine.Id, "月", default);
        var view = await course.ViewAsync(routine.Id, default);

        Assert.Equal("明", view.Next!.Literal);
        Assert.Equal(2, view.Known);
        Assert.Equal(SeenState.Dismissed, _h.Routines.Seen.Single(s => s.Key == "kanji:日").State);
        Assert.Equal(2, _h.UnitOfWork.Saves);
    }

    [Fact]
    public async Task The_course_can_start_from_a_later_school_year()
    {
        var routine = Create("""{"enabled":true,"fromGrade":2}""");

        var view = await Course().ViewAsync(routine.Id, default);

        Assert.Equal("明", view.Next!.Literal);
    }

    [Fact]
    public async Task Without_its_dictionaries_the_course_says_where_they_go()
    {
        var routine = Create();
        _book.Available = false;

        var view = await Course().ViewAsync(routine.Id, default);

        Assert.Equal((false, "/kanji-data", (KanjiNextView?)null), (view.Available, view.Folder, view.Next));
    }

    [Fact]
    public async Task Only_one_kanji_at_a_time_can_be_known()
    {
        var routine = Create();

        await Assert.ThrowsAsync<DomainException>(() => Course().KnownAsync(routine.Id, "日月明", default));
    }

    [Fact]
    public void Settings_without_a_kanji_section_turn_it_off()
    {
        Assert.Equal(new KanjiSettings(false, 1), KanjiSettings.Read("{}"));
        Assert.Equal(new KanjiSettings(true, 1), KanjiSettings.Read("""{"kanji":{"enabled":true,"fromGrade":12}}"""));
    }
}
