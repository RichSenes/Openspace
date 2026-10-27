namespace Openspace.Application.LocalModels;

/// <summary>
/// Models running on this machine's graphics card, started and stopped by Openspace, and the
/// programs that take the card before them.
/// </summary>
public sealed class LocalModelOptions
{
    public const string SectionName = "LocalModels";

    public bool Enabled { get; set; }

    /// <summary>The OpenCode provider desks name local models with: <c>local/gemma</c>.</summary>
    public string Provider { get; set; } = "local";

    /// <summary>Where the model server answers once started (an OpenAI-compatible API).</summary>
    public Uri ServerUrl { get; set; } = new("http://127.0.0.1:8080");

    /// <summary>A file holding the server's API key, when it asks for one.</summary>
    public string? ApiKeyFile { get; set; }

    /// <summary>Where Openspace writes the OpenCode provider for these models; null writes nothing.</summary>
    public string? OpenCodeConfigPath { get; set; }

    public List<LocalModel> Models { get; set; } = [];

    /// <summary>Programs that come before local models: while one runs, the card is theirs.</summary>
    public List<GpuHolder> Holders { get; set; } = [];

    /// <summary>A model nobody used for this long is unloaded, to give the card back.</summary>
    public TimeSpan IdleUnload { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a local task may go on once a program asks for the card.</summary>
    public TimeSpan YieldGrace { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>How long a model may take to load before Openspace gives up on it.</summary>
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>How long a model that failed to load is left alone before trying again.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>A claim not renewed for this long lapses, in case its program died without releasing it.</summary>
    public TimeSpan ClaimLease { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a routine waits for the graphics card (an image generator may have it) before giving up.</summary>
    public TimeSpan RoutineWait { get; set; } = TimeSpan.FromHours(3);

    /// <summary>The small model on the processor that answers typed questions for routines.</summary>
    public DeciderOptions Decider { get; set; } = new();

    public LocalModel? Find(string id) => Models.Find(m => string.Equals(m.Id, id, StringComparison.Ordinal));
}

/// <param name="Id">The model's name after the provider: <c>gemma</c> in <c>local/gemma</c>.</param>
/// <param name="Command">What starts the server with this model; it must keep running in the foreground.</param>
/// <param name="MemoryMiB">Graphics memory it needs: it is not started while less is free.</param>
public sealed class LocalModel
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Command { get; set; } = "";

    public List<string> Arguments { get; set; } = [];

    public int MemoryMiB { get; set; }

    public int Context { get; set; } = 32768;
}

/// <param name="Match">Found in a running program's command line, it means the program is there.</param>
public sealed class GpuHolder
{
    public string Name { get; set; } = "";

    public string Match { get; set; } = "";
}

/// <summary>
/// A model on the processor (Rizzo-Flow, say) that answers typed questions: yes or no, one of
/// a list, a score. It needs no graphics card, so it runs next to anything; Openspace starts it
/// when a routine asks and stops it when nobody does.
/// </summary>
public sealed class DeciderOptions
{
    /// <summary>What starts it; empty means routines have no decider.</summary>
    public string Command { get; set; } = "";

    public List<string> Arguments { get; set; } = [];

    public Uri Url { get; set; } = new("http://127.0.0.1:8017");

    public TimeSpan IdleUnload { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromMinutes(3);
}
