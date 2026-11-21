namespace Openspace.Application.Functions;

/// <summary>
/// Asks an agent engine for text only, with no tools and no files, on a model a function names:
/// what a routine needs when its words are written by the model of Settings › Models.
/// </summary>
public interface IEngineText
{
    /// <param name="engine">Claude Code or OpenCode; local models are asked through <see cref="LocalModels.ILocalChat"/>.</param>
    /// <param name="model">A model of that engine; null for the engine's default.</param>
    Task<string> AskAsync(ModelEngine engine, string? model, string instructions, string text, CancellationToken cancellationToken);
}

/// <summary>
/// Who may read the person's own data (mail, calendar, health): local models and Claude on the
/// person's account, never free online models.
/// </summary>
public static class PersonalDataReaders
{
    /// <summary>
    /// False for OpenCode's free online models, named <c>…-free</c> or <c>…:free</c>, and for
    /// OpenCode's default model, which cannot be told apart from one.
    /// </summary>
    public static bool MayRead(FunctionModel function)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (function.Engine != ModelEngine.OpenCode)
        {
            return true;
        }

        var model = function.Model?.Trim();
        return !string.IsNullOrEmpty(model)
            && !model.EndsWith("-free", StringComparison.OrdinalIgnoreCase)
            && !model.EndsWith(":free", StringComparison.OrdinalIgnoreCase);
    }
}
