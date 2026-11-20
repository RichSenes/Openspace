using System.Text.RegularExpressions;
using Openspace.Domain.Common;

namespace Openspace.Application.LocalModels;

/// <param name="Id">The model's name after the provider: <c>gemma</c> in <c>local/gemma</c>.</param>
/// <param name="Command">What starts the server with this model, such as llama-server.</param>
/// <param name="Arguments">One argument per item, as the command takes them.</param>
/// <param name="MemoryMiB">Graphics memory it needs: it is not started while less is free.</param>
/// <param name="Context">How many tokens of conversation it holds.</param>
public sealed record LocalModelView(string Id, string Name, string Command, IReadOnlyList<string> Arguments, int MemoryMiB, int Context);

/// <param name="Match">Found in a running program's command line, it means the program is there.</param>
public sealed record GpuHolderView(string Name, string Match);

/// <param name="Command">What starts the small model on the processor; empty for none.</param>
public sealed record DeciderView(string Command, IReadOnlyList<string> Arguments, string Url);

/// <summary>The local models as a person sets them in Openspace.</summary>
public sealed record LocalAiSettingsView(
    bool Enabled,
    string ServerUrl,
    IReadOnlyList<LocalModelView> Models,
    IReadOnlyList<GpuHolderView> Holders,
    int IdleUnloadMinutes,
    int YieldGraceMinutes,
    DeciderView Decider);

/// <summary>What a look at one model found: what is fine, and what would keep it from loading.</summary>
public sealed record ModelCheck(bool Ok, IReadOnlyList<string> Fine, IReadOnlyList<string> Problems);

/// <summary>
/// The local models' settings read from the options, checked before they are saved, and one
/// model looked at without loading it: the command is there, its files are there, it fits.
/// </summary>
public static partial class LocalAiSettings
{
    public const int MaxModels = 8;
    public const int MaxHolders = 12;
    public const int MaxMinutes = 24 * 60;

    public static LocalAiSettingsView Of(LocalModelOptions options) => new(
        options.Enabled,
        options.ServerUrl.ToString(),
        [.. options.Models.Select(m => new LocalModelView(m.Id, m.Name, m.Command, [.. m.Arguments], m.MemoryMiB, m.Context))],
        [.. options.Holders.Select(h => new GpuHolderView(h.Name, h.Match))],
        (int)Math.Round(options.IdleUnload.TotalMinutes),
        (int)Math.Round(options.YieldGrace.TotalMinutes),
        new DeciderView(options.Decider.Command, [.. options.Decider.Arguments], options.Decider.Url.ToString()));

    /// <summary>Drops the blank items a shorter list set in Openspace leaves over a longer one below it.</summary>
    public static void DropBlanks(LocalModelOptions options)
    {
        options.Models.RemoveAll(m => string.IsNullOrWhiteSpace(m.Id));
        foreach (var model in options.Models)
        {
            model.Arguments.RemoveAll(string.IsNullOrWhiteSpace);
        }

        options.Holders.RemoveAll(h => string.IsNullOrWhiteSpace(h.Match));
        options.Decider.Arguments.RemoveAll(string.IsNullOrWhiteSpace);
    }

    /// <summary>The settings trimmed and checked; refused with the reason a person can fix.</summary>
    public static LocalAiSettingsView Validate(LocalAiSettingsView request)
    {
        var models = request.Models.Select(m => m with
        {
            Id = m.Id.Trim(),
            Name = m.Name.Trim(),
            Command = m.Command.Trim(),
            Arguments = [.. m.Arguments.Select(a => a.Trim()).Where(a => a.Length > 0)],
        }).ToList();

        if (models.Count > MaxModels)
        {
            throw new DomainException($"At most {MaxModels} local models.");
        }

        foreach (var model in models)
        {
            if (!ModelId().IsMatch(model.Id))
            {
                throw new DomainException($"\"{model.Id}\" cannot be a model's name: use lowercase letters, digits, dots and dashes, as in gemma-4.");
            }

            if (model.Command.Length == 0)
            {
                throw new DomainException($"{model.Id} needs the command that starts it, such as llama-server.");
            }

            if (model.MemoryMiB is < 0 or > 1_048_576)
            {
                throw new DomainException($"{model.Id}: the memory it needs is in MiB, between 0 and 1048576.");
            }

            if (model.Context is < 512 or > 4_194_304)
            {
                throw new DomainException($"{model.Id}: the context is between 512 and 4194304 tokens.");
            }
        }

        if (models.GroupBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twice)
        {
            throw new DomainException($"Two models are called {twice.Key}: each needs a name of its own.");
        }

        var holders = request.Holders
            .Select(h => new GpuHolderView(h.Name.Trim(), h.Match.Trim()))
            .Where(h => h.Name.Length > 0 || h.Match.Length > 0)
            .ToList();
        if (holders.Count > MaxHolders)
        {
            throw new DomainException($"At most {MaxHolders} programs that come first.");
        }

        if (holders.FirstOrDefault(h => h.Name.Length == 0 || h.Match.Length == 0) is { } half)
        {
            throw new DomainException($"A program that comes first needs a name and the words that find it ({(half.Name.Length == 0 ? half.Match : half.Name)}).");
        }

        return request with
        {
            ServerUrl = Address(request.ServerUrl, "The model server"),
            Models = models,
            Holders = holders,
            IdleUnloadMinutes = Minutes(request.IdleUnloadMinutes, "Unloading after"),
            YieldGraceMinutes = Minutes(request.YieldGraceMinutes, "The time a task keeps the card"),
            Decider = new DeciderView(
                request.Decider.Command.Trim(),
                [.. request.Decider.Arguments.Select(a => a.Trim()).Where(a => a.Length > 0)],
                Address(request.Decider.Url, "The decider")),
        };
    }

    /// <summary>
    /// Looks at a model without loading it: whether its command can be found, whether the files
    /// its arguments name exist, whether it fits the card.
    /// </summary>
    /// <param name="exists">Whether a file is there.</param>
    /// <param name="onPath">Whether a bare command is found on the PATH.</param>
    /// <param name="cardMiB">The card's memory, null when it cannot be read.</param>
    public static ModelCheck Check(LocalModelView model, Func<string, bool> exists, Func<string, bool> onPath, int? cardMiB)
    {
        var fine = new List<string>();
        var problems = new List<string>();
        var command = model.Command.Trim();
        if (command.Length == 0)
        {
            problems.Add("No command starts it.");
        }
        else if (command.Contains('/', StringComparison.Ordinal) ? exists(command) : onPath(command))
        {
            fine.Add($"{command} is there.");
        }
        else
        {
            problems.Add(command.Contains('/', StringComparison.Ordinal)
                ? $"{command} does not exist."
                : $"{command} is not on the PATH of Openspace: write its full path.");
        }

        foreach (var file in model.Arguments.Select(a => a.Trim()).Where(LooksLikeFile))
        {
            if (exists(file))
            {
                fine.Add($"{Path.GetFileName(file)} is there.");
            }
            else
            {
                problems.Add($"{file} does not exist.");
            }
        }

        if (cardMiB is { } card && model.MemoryMiB > 0)
        {
            if (model.MemoryMiB > card)
            {
                problems.Add($"It needs {model.MemoryMiB} MiB and the card has {card} MiB.");
            }
            else
            {
                fine.Add($"It needs {model.MemoryMiB} of the card's {card} MiB.");
            }
        }

        return new ModelCheck(problems.Count == 0, fine, problems);
    }

    /// <summary>An argument that names a file to load: an absolute path with an extension, such as a .gguf.</summary>
    private static bool LooksLikeFile(string argument) =>
        argument.StartsWith('/') && Path.HasExtension(argument);

    private static string Address(string text, string what)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            throw new DomainException($"{what} needs an address such as http://127.0.0.1:8080.");
        }

        return url.ToString();
    }

    private static int Minutes(int minutes, string what) =>
        minutes is >= 1 and <= MaxMinutes ? minutes : throw new DomainException($"{what} is between 1 and {MaxMinutes} minutes.");

    [GeneratedRegex("^[a-z0-9]+([.-][a-z0-9]+)*$")]
    private static partial Regex ModelId();
}
