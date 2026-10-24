using Openspace.Infrastructure.Checks;

namespace Openspace.Infrastructure.Tests;

public sealed class ShellCheckRunnerTests
{
    private readonly ShellCheckRunner _runner = new();

    [Fact]
    public async Task A_check_passes_on_exit_zero_and_keeps_errors_in_its_output()
    {
        var run = await _runner.RunAsync(Path.GetTempPath(), "echo building && echo 'one warning' >&2", TimeSpan.FromMinutes(1), default);

        Assert.True(run.Passed);
        Assert.Contains("building", run.Output, StringComparison.Ordinal);
        Assert.Contains("one warning", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failing_check_says_its_exit_code()
    {
        var run = await _runner.RunAsync(Path.GetTempPath(), "echo '2 tests failed'; exit 3", TimeSpan.FromMinutes(1), default);

        Assert.Equal((false, (int?)3), (run.Passed, run.ExitCode));
    }

    [Fact]
    public async Task A_check_that_runs_too_long_is_stopped()
    {
        var run = await _runner.RunAsync(Path.GetTempPath(), "sleep 30", TimeSpan.FromMilliseconds(300), default);

        Assert.Null(run.ExitCode);
        Assert.Contains("[Stopped after", run.Output, StringComparison.Ordinal);
        Assert.True(run.Duration < TimeSpan.FromSeconds(10));
    }
}
