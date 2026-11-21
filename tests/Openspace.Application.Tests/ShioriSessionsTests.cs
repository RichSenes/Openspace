using Openspace.Application.Shiori;
using Openspace.Domain.Offices;

namespace Openspace.Application.Tests;

public class ShioriSessionsTests
{
    private readonly ShioriHarness _s = new();

    private async Task<(ShioriSessions Sessions, ShioriSessionView Session)> OpenAsync(ShioriMode mode = ShioriMode.Brainstorm, string? title = "Merge order")
    {
        var sessions = _s.Sessions();
        return (sessions, await sessions.CreateAsync(title, mode, "elliot", default));
    }

    private static async Task<ShioriSessionView> SayAsync(ShioriSessions sessions, Guid id, string text)
    {
        await sessions.SendAsync(id, text, default);
        await sessions.Completion(id);
        return await sessions.GetAsync(id, default);
    }

    [Fact]
    public async Task A_new_session_gets_its_note_in_Sessions_signed_by_Shiori()
    {
        var (_, session) = await OpenAsync();

        Assert.Equal("Sessions/2015-06-24-merge-order.md", session.Note);
        var note = _s.H.Vault.Notes[session.Note];
        Assert.Equal(("session", "Shiori", "Merge order"), (note.Kind, note.Author, note.Title));
        Assert.Contains("## Transcript", note.Body, StringComparison.Ordinal);
        Assert.Contains("## Whiteboard", note.Body, StringComparison.Ordinal);
        Assert.Equal("Shiori", _s.H.Vault.Writes.Single().Author);
    }

    [Fact]
    public async Task Two_sessions_on_the_same_subject_and_day_get_a_note_each()
    {
        var (sessions, first) = await OpenAsync();

        var second = await sessions.CreateAsync("Merge order", ShioriMode.Brainstorm, "elliot", default);

        Assert.Equal("Sessions/2015-06-24-merge-order-2.md", second.Note);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task A_message_is_answered_in_pieces_and_the_whole_answer_is_saved_in_the_transcript_and_the_note()
    {
        var (sessions, session) = await OpenAsync();
        _s.Runtime.Answers.Enqueue(["Merging in ", "approval order is in [[Decisions/2026-11-20-merge-order]]."]);

        var said = await sessions.SendAsync(session.Id, "  How do we merge?  ", default);
        await sessions.Completion(session.Id);

        Assert.Equal((TranscriptRole.Person, "How do we merge?"), (said.Role, said.Text));
        var now = await sessions.GetAsync(session.Id, default);
        Assert.Equal(
            [(TranscriptRole.Person, "How do we merge?"), (TranscriptRole.Shiori, "Merging in approval order is in [[Decisions/2026-11-20-merge-order]].")],
            now.Transcript.Select(e => (e.Role, e.Text)));
        Assert.False(now.Answering);
        Assert.Equal(["Merging in ", "approval order is in [[Decisions/2026-11-20-merge-order]]."], _s.Publisher.Texts.Select(t => t.Chunk));
        Assert.Equal([TranscriptRole.Person, TranscriptRole.Shiori], _s.Publisher.Messages.Select(m => m.Entry.Role));
        Assert.Equal([true, false], _s.Publisher.States.Select(s => s.Answering));
        var note = _s.H.Vault.Notes[session.Note].Body;
        Assert.Contains("**elliot** · 2015-06-24", note, StringComparison.Ordinal);
        Assert.Contains("approval order is in [[Decisions/2026-11-20-merge-order]].", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_first_answer_starts_the_engine_conversation_and_the_next_resumes_the_same_one()
    {
        var (sessions, session) = await OpenAsync();

        await SayAsync(sessions, session.Id, "Hello");
        await SayAsync(sessions, session.Id, "And then?");

        var (first, second) = (_s.Runtime.Turns[0], _s.Runtime.Turns[1]);
        Assert.False(first.Resume);
        Assert.True(second.Resume);
        Assert.Equal(first.RuntimeSessionId, second.RuntimeSessionId);
        Assert.True(Guid.TryParse(first.RuntimeSessionId, out _));
    }

    [Fact]
    public async Task After_a_restart_the_session_resumes_the_engine_conversation_it_had()
    {
        var (before, session) = await OpenAsync();
        await SayAsync(before, session.Id, "Hello");

        var after = _s.Sessions();
        await SayAsync(after, session.Id, "Where were we?");

        Assert.Equal(_s.Runtime.Turns[0].RuntimeSessionId, _s.Runtime.Turns[1].RuntimeSessionId);
        Assert.True(_s.Runtime.Turns[1].Resume);
        Assert.Equal(4, (await after.GetAsync(session.Id, default)).Transcript.Count);
    }

    [Fact]
    public async Task A_second_message_while_Shiori_answers_is_refused()
    {
        var (sessions, session) = await OpenAsync();
        _s.Runtime.Hold = new TaskCompletionSource();

        await sessions.SendAsync(session.Id, "First", default);
        var refused = await Assert.ThrowsAsync<RejectedException>(() => sessions.SendAsync(session.Id, "Second", default));
        Assert.True((await sessions.GetAsync(session.Id, default)).Answering);
        _s.Runtime.Hold.SetResult();
        await sessions.Completion(session.Id);

        Assert.Equal(ShioriSessions.BusyCode, refused.Code);
        Assert.Equal(["First", "Noted."], (await sessions.GetAsync(session.Id, default)).Transcript.Select(e => e.Text));
    }

    [Fact]
    public async Task An_empty_message_is_refused()
    {
        var (sessions, session) = await OpenAsync();

        var refused = await Assert.ThrowsAsync<RejectedException>(() => sessions.SendAsync(session.Id, "   ", default));

        Assert.Equal("empty-text", refused.Code);
        Assert.Empty(_s.Runtime.Turns);
    }

    [Fact]
    public async Task A_mode_switch_is_recorded_and_the_next_message_carries_the_new_mode_and_its_tools()
    {
        var (sessions, session) = await OpenAsync();
        await SayAsync(sessions, session.Id, "Ideas for the merge order?");

        var switched = await sessions.SetModeAsync(session.Id, ShioriMode.Decide, default);
        await SayAsync(sessions, session.Id, "Let us settle it.");

        Assert.Equal(ShioriMode.Decide, switched.Mode);
        var mode = switched.Transcript[^1];
        Assert.Equal((TranscriptRole.Mode, "Mode: decide", ShioriMode.Decide), (mode.Role, mode.Text, mode.Mode));
        var (before, after) = (_s.Runtime.Turns[0], _s.Runtime.Turns[1]);
        Assert.Equal((ShioriMode.Brainstorm, ShioriMode.Decide), (before.Mode, after.Mode));
        Assert.StartsWith("[Mode: brainstorm] ", before.Text, StringComparison.Ordinal);
        Assert.StartsWith("[Mode: decide] ", after.Text, StringComparison.Ordinal);
        Assert.Contains("## Mode: decide", after.Instructions, StringComparison.Ordinal);
        Assert.DoesNotContain(ShioriTools.ProposeDecision, before.Tools);
        Assert.Contains(ShioriTools.ProposeDecision, after.Tools);
        Assert.Contains("*Mode: decide*", _s.H.Vault.Notes[session.Note].Body, StringComparison.Ordinal);
        Assert.Equal(ShioriMode.Decide, _s.Publisher.States.Last(s => !s.Answering).Mode);
    }

    [Fact]
    public async Task Switching_to_the_mode_already_in_force_records_nothing()
    {
        var (sessions, session) = await OpenAsync(ShioriMode.Decide);

        var same = await sessions.SetModeAsync(session.Id, ShioriMode.Decide, default);

        Assert.Empty(same.Transcript);
    }

    [Fact]
    public async Task Shiori_is_told_who_it_is_the_constitution_the_offices_and_how_to_cite()
    {
        _s.H.Vault.Put("Constitution.md", "# Constitution\n\n1. Read before acting.\n2. Every change comes with a test.\n");
        var (sessions, session) = await OpenAsync(ShioriMode.Organise);

        await SayAsync(sessions, session.Id, "Plan the logout.");

        var instructions = _s.Runtime.Turns[0].Instructions;
        Assert.Contains("You are Shiori", instructions, StringComparison.Ordinal);
        Assert.Contains("2. Every change comes with a test.", instructions, StringComparison.Ordinal);
        Assert.Contains("## Mode: organise", instructions, StringComparison.Ordinal);
        Assert.Contains("- E Corp: Web (1 desk)", instructions, StringComparison.Ordinal);
        Assert.Contains("[[Decisions/2026-11-20-merge-order]]", instructions, StringComparison.Ordinal);
        Assert.Equal(_s.Models.Shiori.Model, _s.Runtime.Turns[0].Model);
    }

    [Fact]
    public async Task A_failed_first_answer_is_recorded_and_the_next_try_starts_a_clean_conversation()
    {
        var (sessions, session) = await OpenAsync();
        _s.Runtime.Fail = new InvalidOperationException("Claude Code stopped: overloaded");

        var failed = await SayAsync(sessions, session.Id, "Hello");
        _s.Runtime.Fail = null;
        await SayAsync(sessions, session.Id, "Hello again");

        Assert.Equal((TranscriptRole.Failed, "Claude Code stopped: overloaded"), (failed.Transcript[^1].Role, failed.Transcript[^1].Text));
        Assert.Equal("Claude Code stopped: overloaded", _s.Publisher.States.First(s => s.Error is not null).Error);
        Assert.NotEqual(_s.Runtime.Turns[0].RuntimeSessionId, _s.Runtime.Turns[1].RuntimeSessionId);
        Assert.False(_s.Runtime.Turns[1].Resume);
    }

    [Fact]
    public async Task Whiteboard_items_are_added_moved_done_and_removed_and_each_change_is_pushed()
    {
        var (sessions, session) = await OpenAsync();
        var id = session.Id;

        var idea = await sessions.ChangeBoardAsync(id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Idea, Text: "Merge in approval order"), default);
        await sessions.ChangeBoardAsync(id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Doubt, Text: "What about hotfixes?"), default);
        await sessions.ChangeBoardAsync(id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Decision, Text: "Hotfixes go first"), default);
        await sessions.ChangeBoardAsync(id, new(WhiteboardAction.Move, Item: idea.Id, Kind: WhiteboardKind.Decision, Position: 1), default);
        await sessions.ChangeBoardAsync(id, new(WhiteboardAction.Done, Item: "W2"), default);
        await sessions.ChangeBoardAsync(id, new(WhiteboardAction.Remove, Item: "w3"), default);

        var board = (await sessions.GetAsync(id, default)).Board;
        Assert.Equal([("w2", WhiteboardKind.Doubt, true), ("w1", WhiteboardKind.Decision, false)], board.Select(i => (i.Id, i.Kind, i.Done)));
        Assert.Equal(6, _s.Publisher.Boards.Count);
        Assert.Equal(3, _s.Publisher.Boards[2].Board.Count);
    }

    [Fact]
    public async Task Moving_to_the_top_of_a_column_puts_the_item_before_the_others_of_that_column()
    {
        var (sessions, session) = await OpenAsync();
        var id = session.Id;
        await sessions.ChangeBoardAsync(id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Idea, Text: "A"), default);
        await sessions.ChangeBoardAsync(id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Doubt, Text: "B"), default);
        await sessions.ChangeBoardAsync(id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Idea, Text: "C"), default);

        await sessions.ChangeBoardAsync(id, new(WhiteboardAction.Move, Item: "w3", Position: 1), default);

        var ideas = (await sessions.GetAsync(id, default)).Board.Where(i => i.Kind == WhiteboardKind.Idea).Select(i => i.Text);
        Assert.Equal(["C", "A"], ideas);
    }

    [Fact]
    public async Task An_unknown_item_or_kind_is_refused_with_what_to_use()
    {
        var (sessions, session) = await OpenAsync();

        var noItem = await Assert.ThrowsAsync<RejectedException>(() => sessions.ChangeBoardAsync(session.Id, new(WhiteboardAction.Done, Item: "w9"), default));
        var noKind = await Assert.ThrowsAsync<RejectedException>(() => sessions.ChangeBoardAsync(session.Id, new(WhiteboardAction.Add, Text: "Something"), default));

        Assert.Equal(("whiteboard-item", "whiteboard-kind"), (noItem.Code, noKind.Code));
    }

    [Fact]
    public async Task Ending_a_session_proposes_its_decisions_and_writes_the_board_into_the_note()
    {
        var (sessions, session) = await OpenAsync();
        await sessions.ChangeBoardAsync(session.Id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Idea, Text: "Batch merges at night"), default);
        await sessions.ChangeBoardAsync(session.Id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Decision, Text: "Merge in approval order", Office: "E Corp"), default);

        var ending = await sessions.EndAsync(session.Id, default);

        var path = Assert.Single(ending.Proposed);
        Assert.Equal("Decisions/2015-06-24-merge-in-approval-order.md", path);
        var proposal = _s.H.Vault.Notes[path];
        Assert.Equal(("decision", "proposed", "Shiori", $"session {session.Id}"), (proposal.Kind, proposal.Status, proposal.Author, proposal.From));
        Assert.Equal(["E Corp"], proposal.Offices);
        Assert.Contains("[[Sessions/2015-06-24-merge-order|Merge order]]", proposal.Body, StringComparison.Ordinal);
        Assert.NotNull(ending.Session.EndedAt);
        Assert.Equal(path, ending.Session.Board.Single(i => i.Kind == WhiteboardKind.Decision).Note);
        var note = _s.H.Vault.Notes[session.Note];
        Assert.Contains("### Ideas\n\n- [ ] Batch merges at night", note.Body, StringComparison.Ordinal);
        Assert.Contains("- [ ] Merge in approval order (E Corp) → [[Decisions/2015-06-24-merge-in-approval-order]]", note.Body, StringComparison.Ordinal);
        Assert.True(_s.Publisher.States[^1].Ended);
    }

    [Fact]
    public async Task Ending_a_session_leaves_its_open_tasks_under_Next_for_tomorrow_morning()
    {
        var (sessions, session) = await OpenAsync();
        await sessions.ChangeBoardAsync(session.Id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Task, Text: "Write the merge script", Office: "E Corp"), default);
        await sessions.ChangeBoardAsync(session.Id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Task, Text: "Ask about Fridays"), default);
        var done = await sessions.ChangeBoardAsync(session.Id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Task, Text: "Read the old plan"), default);
        await sessions.ChangeBoardAsync(session.Id, new(WhiteboardAction.Done, Item: done.Id), default);
        await sessions.ChangeBoardAsync(session.Id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Idea, Text: "Batch merges at night"), default);
        Assert.DoesNotContain("## Next", _s.H.Vault.Notes[session.Note].Body, StringComparison.Ordinal);

        await sessions.EndAsync(session.Id, default);

        var body = _s.H.Vault.Notes[session.Note].Body;
        Assert.Contains("## Next\n\n- [ ] Write the merge script (E Corp)\n- [ ] Ask about Fridays\n", body, StringComparison.Ordinal);
        Assert.Equal(["Write the merge script (E Corp)", "Ask about Fridays"], Routines.Kinds.TomorrowMorning.OpenItems(body));
    }

    [Fact]
    public async Task The_acceptor_takes_the_proposals_of_an_ended_session_like_any_other()
    {
        var (sessions, session) = await OpenAsync();
        await sessions.ChangeBoardAsync(session.Id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Decision, Text: "Merge in approval order", Office: "E Corp"), default);
        var path = (await sessions.EndAsync(session.Id, default)).Proposed.Single();

        using var acceptor = new Attention.DecisionAcceptor(_s.H.Vault);
        var accepted = await acceptor.RunAsync(default);

        Assert.Contains(path, accepted);
        Assert.Equal("accepted", _s.H.Vault.Notes[path].Status);
    }

    [Fact]
    public async Task An_ended_session_takes_no_more_messages_nor_changes_and_ending_again_proposes_nothing()
    {
        var (sessions, session) = await OpenAsync();
        await sessions.ChangeBoardAsync(session.Id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Decision, Text: "Merge in approval order"), default);
        await sessions.EndAsync(session.Id, default);

        var message = await Assert.ThrowsAsync<RejectedException>(() => sessions.SendAsync(session.Id, "One more thing", default));
        var mode = await Assert.ThrowsAsync<RejectedException>(() => sessions.SetModeAsync(session.Id, ShioriMode.Decide, default));
        var again = await sessions.EndAsync(session.Id, default);

        Assert.Equal((ShioriSessions.EndedCode, ShioriSessions.EndedCode), (message.Code, mode.Code));
        Assert.Empty(again.Proposed);
        Assert.Single(_s.H.Vault.Notes.Keys, k => k.StartsWith("Decisions/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ending_while_Shiori_answers_stops_the_answer_and_keeps_nothing_of_it()
    {
        var (sessions, session) = await OpenAsync();
        _s.Runtime.Hold = new TaskCompletionSource();
        await sessions.SendAsync(session.Id, "Tell me everything", default);

        await sessions.EndAsync(session.Id, default);
        await sessions.Completion(session.Id);

        Assert.Equal([_s.Runtime.Turns[0].RuntimeSessionId], _s.Runtime.Stopped);
        Assert.Equal([TranscriptRole.Person, TranscriptRole.Ended], (await sessions.GetAsync(session.Id, default)).Transcript.Select(e => e.Role));
    }

    [Fact]
    public async Task Sessions_are_listed_the_latest_first_with_how_many_messages_they_hold()
    {
        var (sessions, older) = await OpenAsync(title: "Merge order");
        _s.H.Clock.Advance(TimeSpan.FromMinutes(5));
        var newer = await sessions.CreateAsync(null, ShioriMode.Decide, "elliot", default);
        _s.H.Clock.Advance(TimeSpan.FromMinutes(5));
        await SayAsync(sessions, older.Id, "Hello");

        var list = await sessions.ListAsync(default);

        Assert.Equal([older.Id, newer.Id], list.Select(s => s.Id));
        Assert.Equal(2, list[0].Messages);
        Assert.Equal("Session of 2015-06-24 09:05", newer.Title);
    }

    [Fact]
    public async Task An_unknown_session_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _s.Sessions().GetAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public void Each_mode_allows_its_own_tools_and_reading_in_all()
    {
        Assert.Equal([ShioriTools.SearchBrain, ShioriTools.ReadNote, ShioriTools.ListOffices, ShioriTools.Whiteboard], ShioriTools.For(ShioriMode.Brainstorm));
        Assert.Contains(ShioriTools.AcceptDecision, ShioriTools.For(ShioriMode.Decide));
        Assert.DoesNotContain(ShioriTools.PlanWork, ShioriTools.For(ShioriMode.Decide));
        Assert.Contains(ShioriTools.PlanWork, ShioriTools.For(ShioriMode.Organise));
        Assert.DoesNotContain(ShioriTools.ProposeDecision, ShioriTools.For(ShioriMode.Organise));
        Assert.True(ShioriTools.For(ShioriMode.Organise).All(ShioriTools.All.Contains));
    }

    [Fact]
    public void The_prompt_says_when_there_are_no_rules_or_offices_and_flags_offices_without_a_repository()
    {
        var bare = ShioriPrompt.Instructions(ShioriMode.Brainstorm, null, []);
        var office = Office.Open("Allsafe", Harness.Start);

        var withOffice = ShioriPrompt.Instructions(ShioriMode.Brainstorm, "", [office]);

        Assert.Contains("The constitution has no rules yet.", bare, StringComparison.Ordinal);
        Assert.Contains("There are no offices yet.", bare, StringComparison.Ordinal);
        Assert.Contains("You create nothing in the brain or in the offices", bare, StringComparison.Ordinal);
        Assert.Contains("- Allsafe: no rooms yet, no repository yet", withOffice, StringComparison.Ordinal);
    }
}
