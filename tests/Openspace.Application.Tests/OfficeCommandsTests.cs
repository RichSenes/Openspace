using Openspace.Application.Abstractions;

namespace Openspace.Application.Tests;

public class OfficeCommandsTests
{
    private readonly Harness _h = new(withRepository: false);

    private static readonly RepositoryProblem NotARepository = new("not-a-repository", "Not a git repository yet.", true);

    [Fact]
    public async Task A_usable_repository_is_saved()
    {
        await _h.OfficeCommands().SetRepositoryAsync(_h.Office.Id, "/srv/repos/e-corp", initialize: false, default);

        Assert.Equal("/srv/repos/e-corp", _h.Office.RepositoryPath);
        Assert.Equal(1, _h.UnitOfWork.Saves);
    }

    [Fact]
    public async Task A_folder_that_is_not_a_repository_is_refused_with_its_code()
    {
        _h.Workspaces.Problem = NotARepository;

        var error = await Assert.ThrowsAsync<RejectedException>(() =>
            _h.OfficeCommands().SetRepositoryAsync(_h.Office.Id, "/srv/repos/empty", initialize: false, default));

        Assert.Equal("not-a-repository", error.Code);
        Assert.Equal(0, _h.UnitOfWork.Saves);
        Assert.Empty(_h.Workspaces.Initialized);
    }

    [Fact]
    public async Task Asking_to_initialize_turns_the_folder_into_a_repository()
    {
        _h.Workspaces.Problem = NotARepository;

        await _h.OfficeCommands().SetRepositoryAsync(_h.Office.Id, "/srv/repos/empty", initialize: true, default);

        Assert.Equal(["/srv/repos/empty"], _h.Workspaces.Initialized);
        Assert.Equal(1, _h.UnitOfWork.Saves);
    }

    [Fact]
    public async Task A_missing_folder_is_never_initialized()
    {
        _h.Workspaces.Problem = new RepositoryProblem("folder-missing", "The folder does not exist.", false);

        var error = await Assert.ThrowsAsync<RejectedException>(() =>
            _h.OfficeCommands().SetRepositoryAsync(_h.Office.Id, "/srv/repos/nowhere", initialize: true, default));

        Assert.Equal("folder-missing", error.Code);
        Assert.Empty(_h.Workspaces.Initialized);
    }
}
