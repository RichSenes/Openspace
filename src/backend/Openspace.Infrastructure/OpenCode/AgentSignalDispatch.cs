using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Openspace.Application.Agents;

namespace Openspace.Infrastructure.OpenCode;

/// <summary>Hands a signal from any engine to the application, in a scope of its own.</summary>
internal static partial class AgentSignalDispatch
{
    public static async Task DispatchAsync(IServiceScopeFactory scopes, AgentSignal signal, ILogger logger, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var signals = scope.ServiceProvider.GetRequiredService<AgentSignals>();
        try
        {
            var work = signal switch
            {
                AgentSignal.Progress p => signals.ProgressAsync(p.SessionId, p.Step, p.Doing, cancellationToken),
                AgentSignal.PermissionAsked p => signals.PermissionAskedAsync(p.SessionId, p.PermissionId, p.Action, cancellationToken),
                AgentSignal.Finished f => signals.FinishedAsync(f.SessionId, cancellationToken),
                AgentSignal.PermissionReplied r => signals.PermissionRepliedAsync(r.SessionId, r.PermissionId, r.Allowed, cancellationToken),
                AgentSignal.ToolFinished t => signals.ToolFinishedAsync(t.SessionId, t.CallId, t.Kind, t.Summary, t.Detail, t.Succeeded, cancellationToken),
                AgentSignal.Failed f => signals.FailedAsync(f.SessionId, f.Reason, cancellationToken),
                AgentSignal.Compacted c => signals.CompactedAsync(c.SessionId, cancellationToken),
                AgentSignal.OutOfLimits o => signals.OutOfLimitsAsync(o.SessionId, o.Scope, o.Until, o.Reason, cancellationToken),
                AgentSignal.Used u => signals.UsedAsync(u.SessionId, u.Tokens, cancellationToken),
                AgentSignal.Context c => ContextAsync(scope.ServiceProvider, signals, c, cancellationToken),
                _ => Task.CompletedTask,
            };
            await work.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogHandlingFailed(logger, ex, signal.GetType().Name, signal.SessionId);
        }
    }

    private static async Task ContextAsync(IServiceProvider services, AgentSignals signals, AgentSignal.Context context, CancellationToken cancellationToken)
    {
        var window = context.Window;
        if (window is null && context.Model is { } model)
        {
            window = await services.GetRequiredService<OpenCodeContextWindows>()
                .ForAsync(model, services.GetRequiredService<OpenCodeRuntime>(), cancellationToken).ConfigureAwait(false);
        }

        await signals.ContextAsync(context.SessionId, context.Tokens, window, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not handle {Signal} for session {SessionId}")]
    private static partial void LogHandlingFailed(ILogger logger, Exception exception, string signal, string sessionId);
}
