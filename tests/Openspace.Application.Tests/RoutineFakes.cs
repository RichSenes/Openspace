using System.Text.Json;
using Openspace.Application.Abstractions;
using Openspace.Application.Routines;
using Openspace.Domain.Offices;
using Openspace.Domain.Routines;

namespace Openspace.Application.Tests;

internal sealed class InMemoryRoutines : IRoutineRepository
{
    public List<Routine> Items { get; } = [];

    public List<RoutineRun> Runs { get; } = [];

    public List<Proposal> Proposals { get; } = [];

    public List<SeenItem> Seen { get; } = [];

    public Task<Routine?> FindAsync(RoutineId id, CancellationToken cancellationToken) => Task.FromResult(Items.Find(r => r.Id == id));

    public Task<IReadOnlyList<Routine>> ListAsync(OfficeId officeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Routine>>([.. Items.Where(r => r.OfficeId == officeId).OrderBy(r => r.Name)]);

    public Task<IReadOnlyList<OfficeId>> OfficesWithRoutinesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OfficeId>>([.. Items.Select(r => r.OfficeId).Distinct()]);

    public Task<IReadOnlyList<Routine>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Routine>>([.. Items.Where(r => r.IsDue(now)).OrderBy(r => r.NextRunAt)]);

    public void Add(Routine routine) => Items.Add(routine);

    public void Remove(Routine routine) => Items.Remove(routine);

    public Task<RoutineRun?> FindRunAsync(RoutineRunId id, CancellationToken cancellationToken) => Task.FromResult(Runs.Find(r => r.Id == id));

    public Task<IReadOnlyList<RoutineRun>> ListRunsAsync(RoutineId routineId, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RoutineRun>>([.. Runs.Where(r => r.RoutineId == routineId).OrderByDescending(r => r.StartedAt).Take(limit)]);

    public Task<IReadOnlyList<RoutineRun>> ListUnfinishedRunsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RoutineRun>>([.. Runs.Where(r => r.Status == RoutineRunStatus.Running)]);

    public void AddRun(RoutineRun run) => Runs.Add(run);

    public Task<Proposal?> FindProposalAsync(ProposalId id, CancellationToken cancellationToken) => Task.FromResult(Proposals.Find(p => p.Id == id));

    public Task<IReadOnlyList<Proposal>> ListProposalsAsync(OfficeId officeId, ProposalStatus? status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Proposal>>([.. Proposals.Where(p => p.OfficeId == officeId && (status is null || p.Status == status)).OrderByDescending(p => p.CreatedAt)]);

    public void AddProposal(Proposal proposal) => Proposals.Add(proposal);

    public Task<SeenItem?> FindSeenAsync(RoutineId routineId, string key, CancellationToken cancellationToken) =>
        Task.FromResult(Seen.Find(s => s.RoutineId == routineId && s.Key == key));

    public Task<IReadOnlyList<SeenItem>> ListSeenAsync(RoutineId routineId, string prefix, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SeenItem>>([.. Seen.Where(s => s.RoutineId == routineId && s.Key.StartsWith(prefix, StringComparison.Ordinal))]);

    public void AddSeen(SeenItem item) => Seen.Add(item);
}

internal sealed class RecordingProgress : IRoutineProgress
{
    public List<(string Routine, string Line)> Lines { get; } = [];

    public void Report(Routine routine, string line) => Lines.Add((routine.Name, line));
}

internal sealed class RecordingOutbox : IRoutineOutbox
{
    public List<(string Routine, string Text, string? File)> Sent { get; } = [];

    public Task<bool> SendAsync(Routine routine, string text, string? filePath, CancellationToken cancellationToken)
    {
        Sent.Add((routine.Name, text, filePath));
        return Task.FromResult(true);
    }
}

/// <summary>Job offers made up for the tests, proposed once each, as the real routine must.</summary>
internal sealed class FakeOffersKind : IRoutineKind
{
    public string Key => "fake-offers";

    public string Name => "Offers of the day";

    public string Description => "Made-up offers, each shown once.";

    public string DefaultSettings => """{"country":"JP"}""";

    public List<(string Url, string Title, string Detail)> Offers { get; } = [];

    public List<string> CarriedOut { get; } = [];

    public Exception? Fail { get; set; }

    /// <summary>Makes a run wait until it is stopped.</summary>
    public bool Hang { get; set; }

    public Exception? FailCarry { get; set; }

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        if (Fail is not null)
        {
            throw Fail;
        }

        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        var shown = 0;
        foreach (var (url, title, detail) in Offers)
        {
            if (!await context.MeetAsync(url, RoutineContext.Fingerprint(title, detail), title, cancellationToken))
            {
                continue;
            }

            await context.ProposeAsync("tailor-cv", title, detail, new { url }, cancellationToken);
            await context.MarkAsync(url, SeenState.Shown, cancellationToken);
            shown++;
        }

        await context.DeliverAsync($"{shown} new offers", null, cancellationToken);
        return $"{shown} new offers";
    }

    /// <summary>Saving a tailored CV in its own folder can be undone; set it to try that path.</summary>
    public bool Undoable { get; set; }

    public bool CanUndo(string action) => Undoable && action == "tailor-cv";

    public Task<string> CarryOutAsync(Proposal proposal, RoutineContext context, CancellationToken cancellationToken)
    {
        if (FailCarry is not null)
        {
            throw FailCarry;
        }

        CarriedOut.Add(proposal.Title);
        return Task.FromResult("Tailored CV saved.");
    }

    public Task RejectedAsync(Proposal proposal, RoutineContext context, CancellationToken cancellationToken) =>
        context.MarkAsync(JsonDocument.Parse(proposal.Payload).RootElement.GetProperty("url").GetString()!, SeenState.Dismissed, cancellationToken);
}

/// <summary>Three kanji: 日 and 月 in the first school year, 明 in the second.</summary>
internal sealed class FakeKanjiBook : IKanjiBook
{
    public bool Available { get; set; } = true;

    public string Folder => "/kanji-data";

    public Task<IReadOnlyList<KanjiInCourse>> CurriculumAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<KanjiInCourse>>([new("日", 1, ["day", "sun"]), new("月", 1, ["month", "moon"]), new("明", 2, ["bright"])]);

    public Task<KanjiEntry?> LookUpAsync(string literal, CancellationToken cancellationToken) =>
        Task.FromResult<KanjiEntry?>(literal switch
        {
            "日" => new KanjiEntry(
                "日", 1, 2, ["day", "sun"], ["ニチ"], ["ひ"], ["M31.5,24.5c1,1,1.7,2.7,1.7,4.7", "M33.4,26c0.8,0,37-3,40-3"], [],
                [new KanjiWord("毎日", "まいにち", ["every day"])],
                [new KanjiSentence("毎日歩きます。", "[毎日|まい|にち][歩|ある]きます。", null, "I walk every day."),
                 new KanjiSentence("日曜日が好き。", "[日曜日|にち|よう|び]が[好|す]き。", "Mi piace la domenica.", "I like Sundays.")]),
            "月" => new KanjiEntry("月", 1, 1, ["month", "moon"], ["ゲツ"], ["つき"], ["M10,10L20,90\"/><script>alert(1)</script><path d=\"M0,0"], [], [], []),
            "明" => new KanjiEntry("明", 2, 2, ["bright"], ["メイ"], ["あか(るい)"], ["M10,20L12,60", "M50,15L52,90"], ["日", "月"], [], []),
            _ => null,
        });
}

/// <summary>A made-up mailbox: two ids a page, archived mail leaves the inbox.</summary>
internal sealed class FakeMailbox : IMailbox
{
    public List<MailMessage> Messages { get; } = [];

    public List<(string Id, string Label, bool Archive)> Labelled { get; } = [];

    public List<MailReply> Drafts { get; } = [];

    public List<string> Accounts { get; } = [];

    public List<string> Queries { get; } = [];

    /// <summary>
    /// Two ids a page, so the routine has to turn pages, newest first. Archived mail leaves the inbox; "in:sent"
    /// finds the person's own, the rest only others'; "newer_than:Nd" and "older_than:Nd" count days back from the start.
    /// </summary>
    public Task<MailPage> FindAsync(string account, string query, string? page, CancellationToken cancellationToken)
    {
        Accounts.Add(account);
        Queries.Add(query);
        var sent = query.Contains("in:sent", StringComparison.Ordinal);
        var matching = Messages
            .Where(m => m.Sent == sent)
            .Where(m => !Labelled.Any(l => l.Id == m.Id && l.Archive))
            .Where(m => Days(query, "newer_than:") is not { } newer || m.Received >= Harness.Start.AddDays(-newer))
            .Where(m => Days(query, "older_than:") is not { } older || m.Received < Harness.Start.AddDays(-older))
            .OrderByDescending(m => m.Received)
            .Select(m => m.Id)
            .ToList();
        var from = page is null ? 0 : int.Parse(page, System.Globalization.CultureInfo.InvariantCulture);
        var next = from + 2 < matching.Count ? (from + 2).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return Task.FromResult(new MailPage([.. matching.Skip(from).Take(2)], next));
    }

    public Task<MailMessage> ReadAsync(string account, string messageId, CancellationToken cancellationToken) =>
        Task.FromResult(Messages.Single(m => m.Id == messageId));

    public Task<IReadOnlyList<MailMessage>> ReadThreadAsync(string account, string threadId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MailMessage>>([.. Messages.Where(m => m.ThreadId == threadId).OrderBy(m => m.Received)]);

    private static int? Days(string query, string word)
    {
        var at = query.IndexOf(word, StringComparison.Ordinal);
        return at < 0 ? null : int.Parse(query[(at + word.Length)..].Split(' ')[0].TrimEnd('d'), System.Globalization.CultureInfo.InvariantCulture);
    }

    public Task LabelAsync(string account, string messageId, string label, bool archive, CancellationToken cancellationToken)
    {
        Accounts.Add(account);
        Labelled.Add((messageId, label, archive));
        return Task.CompletedTask;
    }

    public Task<string> DraftReplyAsync(string account, MailReply reply, CancellationToken cancellationToken)
    {
        Accounts.Add(account);
        Drafts.Add(reply);
        return Task.FromResult($"draft-{Drafts.Count}");
    }
}
