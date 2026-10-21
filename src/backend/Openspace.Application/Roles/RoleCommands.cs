using Openspace.Application.Abstractions;
using Openspace.Domain.Roles;

namespace Openspace.Application.Roles;

/// <summary>Defines the kinds of agents desks can host.</summary>
public sealed class RoleCommands(
    IRoleRepository roles,
    IOfficeRepository offices,
    IUnitOfWork unitOfWork,
    IAgentRuntime runtime,
    TimeProvider clock)
{
    public async Task<RoleId> DefineAsync(
        string name, string? description, string? instructions, string? model, RolePermissions permissions,
        CancellationToken cancellationToken)
    {
        await EnsureUniqueAsync(name, null, cancellationToken).ConfigureAwait(false);
        var role = Role.Define(name, description, instructions, model, permissions, clock.GetUtcNow());
        roles.Add(role);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return role.Id;
    }

    public async Task ChangeAsync(
        RoleId id, string name, string? description, string? instructions, string? model, RolePermissions permissions,
        CancellationToken cancellationToken)
    {
        var role = await roles.GetAsync(id, cancellationToken).ConfigureAwait(false);
        await EnsureUniqueAsync(name, id, cancellationToken).ConfigureAwait(false);
        role.Change(name, description, instructions, model, permissions, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes a role no desk uses any more.</summary>
    public async Task DeleteAsync(RoleId id, CancellationToken cancellationToken)
    {
        var role = await roles.GetAsync(id, cancellationToken).ConfigureAwait(false);
        var users = (await offices.ListAsync(cancellationToken).ConfigureAwait(false)).Where(o => o.UsesRole(id)).Select(o => o.Name).ToList();
        if (users.Count > 0)
        {
            throw new RejectedException(
                "role-in-use", $"Role '{role.Name}' is still used by desks in {string.Join(", ", users)}. Give those desks another role first.");
        }

        roles.Remove(role);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asks the AI to write role instructions from a short description, for a person to review.</summary>
    public async Task<string> DraftInstructionsAsync(string name, string description, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new RejectedException("empty-text", "Describe the role in a few words first.");
        }

        const string Writer = """
            You write instructions for AI coding agents that work inside a software team.
            Answer with the instructions only, in English, as plain text with short bullet points:
            what the agent is responsible for, how it should work, what it must never do, and what to check before finishing.
            No introduction, no closing remarks, no Markdown headings, at most 15 bullets.
            """;
        var draft = await runtime
            .DraftAsync(Writer, $"Role name: {name.Trim()}\nWhat this agent does: {description.Trim()}", cancellationToken)
            .ConfigureAwait(false);
        return draft.Trim();
    }

    private async Task EnsureUniqueAsync(string name, RoleId? self, CancellationToken cancellationToken)
    {
        var all = await roles.ListAsync(cancellationToken).ConfigureAwait(false);
        if (all.Any(r => r.Id != self && string.Equals(r.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new RejectedException("role-name-taken", $"There is already a role named '{name?.Trim()}'.");
        }
    }
}
