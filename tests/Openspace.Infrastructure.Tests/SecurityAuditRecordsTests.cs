using Openspace.Infrastructure.Security;

namespace Openspace.Infrastructure.Tests;

public sealed class SecurityAuditRecordsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "openspace-audits-" + Guid.NewGuid().ToString("N"));

    private SecurityAuditRecords Records() => new(new LiveOptions<SecurityAuditOptions>(new SecurityAuditOptions { Folder = _root }));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private const string Findings = """
        [
          {"verdict":"confirmed","fingerprint":"a1","title":"Invoices readable across users","description":"Any user reads any invoice.",
           "trace":[{"kind":"source","file":"src/api/invoices.ts","line":42,"scope":"get","description":"no owner check"}],
           "severity":{"likelihood":{"score":4,"reason":""},"impact":{"score":4,"reason":""},"overall_severity":"high"},
           "remediation":{"strategy":"Check the owner before returning the invoice."}},
          {"verdict":"confirmed","fingerprint":"a2","title":"Admin route skips auth","description":"x",
           "severity":{"overall_severity":"high"}},
          {"verdict":"needs_validation","fingerprint":"b1","title":"Prompt injection in summarizer","description":"y"},
          {"verdict":"rejected","fingerprint":"c1","title":"Token reuse after logout","description":"z"}
        ]
        """;

    [Fact]
    public void Runs_are_numbered_in_their_project_folder_and_name_their_task()
    {
        var at = new DateTimeOffset(2015, 6, 24, 9, 0, 0, TimeSpan.Zero);
        var task = Guid.NewGuid();

        var first = Records().Open("e-corp", at);
        Records().Link(first, task);
        var second = Records().Open("e-corp", at.AddDays(1));
        var other = Records().Open("allsafe", at);

        Assert.Equal(Path.Combine(_root, "e-corp", "run-1"), first);
        Assert.Equal(Path.Combine(_root, "e-corp", "run-2"), second);
        Assert.Equal(Path.Combine(_root, "allsafe", "run-1"), other);
        var run = Records().List("e-corp")[0];
        Assert.Equal((1, task, at), (run.Number, run.TaskId, run.StartedAt));
    }

    [Fact]
    public void A_finished_run_counts_its_verdicts_and_confirmed_severities_and_has_its_report()
    {
        var folder = Records().Open("e-corp", DateTimeOffset.UnixEpoch);
        File.WriteAllText(Path.Combine(folder, "findings.json"), Findings);
        File.WriteAllText(Path.Combine(folder, "run-metadata.json"), """{"profile":"quick","source_ref":"d2a8875"}""");
        File.WriteAllText(Path.Combine(folder, "REPORT.md"), "# Report\n");

        var run = Assert.Single(Records().List("e-corp"));
        var findings = Records().Findings("e-corp", 1);

        Assert.Equal((2, 1, 1, "quick", "d2a8875", true), (run.Confirmed, run.NeedsValidation, run.Rejected, run.Profile, run.SourceRef, run.HasReport));
        Assert.Equal(2, run.Severities["high"]);
        Assert.Equal("# Report\n", Records().Report("e-corp", 1));
        var invoice = findings[0];
        Assert.Equal(("src/api/invoices.ts:42", "high", "Check the owner before returning the invoice."), (invoice.Location, invoice.Severity, invoice.Remediation));
        Assert.Null(findings[2].Severity);
    }

    [Fact]
    public void A_run_still_being_written_reads_as_empty_and_unknown_projects_have_no_runs()
    {
        var folder = Records().Open("e-corp", DateTimeOffset.UnixEpoch);
        File.WriteAllText(Path.Combine(folder, "findings.json"), "[{\"verdict\":\"conf");

        var run = Assert.Single(Records().List("e-corp"));

        Assert.Equal((0, false), (run.Confirmed, run.HasReport));
        Assert.Empty(Records().Findings("e-corp", 1));
        Assert.Null(Records().Report("e-corp", 1));
        Assert.Empty(Records().List("nobody"));
    }

    [Theory]
    [InlineData("../etc")]
    [InlineData("a/b")]
    [InlineData("")]
    public void A_project_is_a_single_folder_name(string project)
    {
        Assert.Throws<ArgumentException>(() => Records().List(project));
    }
}
