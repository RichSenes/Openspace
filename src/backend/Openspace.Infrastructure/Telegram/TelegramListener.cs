using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Telegram;

/// <summary>
/// Listens to the buttons pressed under routines' messages, on the bots of routines that ask in
/// the chat and no other, so a bot read elsewhere is left alone. Only presses in the person's own
/// chat count: each says yes or no to its proposal, and the buttons give way to the answer.
/// </summary>
internal sealed partial class TelegramListener(
    IHttpClientFactory clients, IOptionsMonitor<TelegramOptions> options, IServiceScopeFactory scopes, TimeProvider clock, ILogger<TelegramListener> logger)
    : BackgroundService
{
    public const string HttpClientName = "telegram-listener";

    /// <summary>What a pressed answer's buttons send back: nothing more to do.</summary>
    public const string Done = "done";

    private static readonly TimeSpan Rest = TimeSpan.FromSeconds(3);

    /// <summary>How often the bots to listen to are looked up again, as routines change.</summary>
    private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, long> _offsets = new(StringComparer.Ordinal);

    private IReadOnlyList<string> _channels = [];

    private DateTimeOffset _refreshed = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenOnceAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(Rest, clock, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogFailed(logger, ex);
                await Task.Delay(Rest * 10, clock, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Reads each listened bot's new presses once and answers them.</summary>
    internal async Task ListenOnceAsync(CancellationToken cancellationToken)
    {
        var folder = options.CurrentValue.Folder;
        if (!File.Exists(Path.Combine(folder, "telegram.env")) || !File.Exists(Path.Combine(folder, "telegram.json")))
        {
            return;
        }

        if (clock.GetUtcNow() - _refreshed >= Refresh)
        {
            using var scope = scopes.CreateScope();
            _channels = await scope.ServiceProvider.GetRequiredService<IChatAnswers>().ChannelsAsync(cancellationToken).ConfigureAwait(false);
            _refreshed = clock.GetUtcNow();
        }

        foreach (var channel in _channels)
        {
            try
            {
                await ListenAsync(folder, channel, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogChannelFailed(logger, ex, channel);
            }
        }
    }

    private async Task ListenAsync(string folder, string channel, CancellationToken cancellationToken)
    {
        var (token, chat, _) = Bots.Read(folder, channel);
        var http = clients.CreateClient(HttpClientName);
        var asked = new Dictionary<string, string> { ["timeout"] = "0", ["allowed_updates"] = """["callback_query"]""" };
        if (_offsets.TryGetValue(channel, out var offset))
        {
            asked["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        JsonElement answer;
        using (var form = new FormUrlEncodedContent(asked))
        {
            answer = await Bots.CallAsync(http, token, "getUpdates", form, cancellationToken).ConfigureAwait(false);
        }

        if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty("result", out var updates) || updates.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var update in updates.EnumerateArray())
        {
            _offsets[channel] = update.GetProperty("update_id").GetInt64() + 1;
            if (update.TryGetProperty("callback_query", out var press))
            {
                await AnswerAsync(http, token, chat, press, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task AnswerAsync(HttpClient http, string token, string chat, JsonElement press, CancellationToken cancellationToken)
    {
        var id = press.GetProperty("id").GetString() ?? string.Empty;
        var data = press.TryGetProperty("data", out var d) ? d.GetString() ?? string.Empty : string.Empty;
        var message = press.TryGetProperty("message", out var m) ? m : default;
        var fromChat = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("chat", out var c) ? c.GetProperty("id").GetRawText() : null;

        // A press anywhere but the person's own chat says nothing.
        ChatAnswer? reply = null;
        if (fromChat == chat && data != Done)
        {
            using var scope = scopes.CreateScope();
            reply = await scope.ServiceProvider.GetRequiredService<IChatAnswers>().AnswerAsync(data, cancellationToken).ConfigureAwait(false);
        }

        var said = new Dictionary<string, string> { ["callback_query_id"] = id };
        if (reply is not null)
        {
            said["text"] = reply.Notice;
        }

        using (var form = new FormUrlEncodedContent(said))
        {
            await Bots.CallAsync(http, token, "answerCallbackQuery", form, cancellationToken).ConfigureAwait(false);
        }

        if (reply is not null && message.TryGetProperty("message_id", out var messageId))
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["chat_id"] = chat,
                ["message_id"] = messageId.GetRawText(),
                ["reply_markup"] = TelegramOutbox.Keyboard([new OutboxButton(reply.Button, Done)]),
            });
            await Bots.CallAsync(http, token, "editMessageReplyMarkup", form, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not listen to the chat")]
    private static partial void LogFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the answers given to the bot {Channel}")]
    private static partial void LogChannelFailed(ILogger logger, Exception ex, string channel);
}
