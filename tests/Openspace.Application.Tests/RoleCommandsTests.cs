using Openspace.Domain.Roles;

namespace Openspace.Application.Tests;

public class RoleCommandsTests
{
    private readonly Harness _h = new();

    [Fact]
    public async Task Roles_are_defined_and_changed()
    {
        var id = await _h.RoleCommands().DefineAsync("Tester", "Writes tests", "Write tests first.", null, RolePermissions.Default, default);

        await _h.RoleCommands().ChangeAsync(id, "QA", "Checks quality", "Run every test.", "opencode/small", RolePermissions.Default, default);

        var role = Assert.Single(_h.Roles.Items);
        Assert.Equal(("QA", "Run every test.", "opencode/small"), (role.Name, role.Instructions, role.Model));
    }

    [Fact]
    public async Task Role_names_are_unique_ignoring_case()
    {
        await _h.RoleCommands().DefineAsync("Tester", null, null, null, RolePermissions.Default, default);

        var error = await Assert.ThrowsAsync<RejectedException>(() =>
            _h.RoleCommands().DefineAsync(" tester ", null, null, null, RolePermissions.Default, default));

        Assert.Equal("role-name-taken", error.Code);
    }

    [Fact]
    public async Task A_role_in_use_cannot_be_deleted_until_its_desks_let_it_go()
    {
        var id = await _h.RoleCommands().DefineAsync("Tester", null, null, null, RolePermissions.Default, default);
        await _h.OfficeCommands().SetDeskRoleAsync(_h.Office.Id, _h.Desk.Id, id, default);

        var error = await Assert.ThrowsAsync<RejectedException>(() => _h.RoleCommands().DeleteAsync(id, default));
        await _h.OfficeCommands().SetDeskRoleAsync(_h.Office.Id, _h.Desk.Id, null, default);
        await _h.RoleCommands().DeleteAsync(id, default);

        Assert.Equal("role-in-use", error.Code);
        Assert.Contains("E Corp", error.Message, StringComparison.Ordinal);
        Assert.Empty(_h.Roles.Items);
    }

    [Fact]
    public async Task Desks_only_take_roles_that_exist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _h.OfficeCommands().SetDeskRoleAsync(_h.Office.Id, _h.Desk.Id, RoleId.New(), default));
    }

    [Fact]
    public async Task Drafting_sends_the_name_and_description_and_returns_trimmed_text()
    {
        _h.Runtime.Draft = "  - Check every form.\n";

        var draft = await _h.RoleCommands().DraftInstructionsAsync("Accessibility reviewer", "Checks pages against WCAG AA", default);

        Assert.Equal("- Check every form.", draft);
        var (_, text) = Assert.Single(_h.Runtime.DraftRequests);
        Assert.Contains("Accessibility reviewer", text, StringComparison.Ordinal);
        Assert.Contains("WCAG AA", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Drafting_needs_a_description()
    {
        var error = await Assert.ThrowsAsync<RejectedException>(() => _h.RoleCommands().DraftInstructionsAsync("Tester", " ", default));

        Assert.Equal("empty-text", error.Code);
    }
}
