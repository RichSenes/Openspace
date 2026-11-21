namespace Openspace.Application.Telegram;

/// <summary>A bot routines can write with: its name in Openspace and its topic in the group. Never its token.</summary>
/// <param name="Topic">The topic of the group it writes in; null for the group's main chat.</param>
public sealed record TelegramBotView(string Name, long? Topic);

/// <param name="Chat">The group's number; null until it is chosen.</param>
public sealed record TelegramStatusView(string Folder, long? Chat, IReadOnlyList<TelegramBotView> Bots);

public sealed record TelegramTopicView(long Id, string Name);

/// <summary>A chat a bot was added to or written in, as Telegram tells it, with the topics seen there.</summary>
public sealed record TelegramChatView(long Id, string Title, string Kind, IReadOnlyList<TelegramTopicView> Topics);

/// <summary>
/// Setting up the person's Telegram from the interface: the bots (each token goes straight to a
/// file only the person can read and never comes back), the group they write in and each bot's
/// topic. Every answer names bots, never their tokens.
/// </summary>
public interface ITelegramSetup
{
    Task<TelegramStatusView> StatusAsync(CancellationToken cancellationToken);

    /// <summary>Asks Telegram whether the token works, then keeps it under <paramref name="name"/>.</summary>
    /// <returns>The bot's own Telegram username.</returns>
    Task<string> AddBotAsync(string name, string token, long? topic, CancellationToken cancellationToken);

    Task RemoveBotAsync(string name, CancellationToken cancellationToken);

    Task SetChatAsync(long chat, CancellationToken cancellationToken);

    /// <param name="topic">Null for the group's main chat.</param>
    Task SetTopicAsync(string name, long? topic, CancellationToken cancellationToken);

    /// <summary>The chats the bot was added to or written in lately, for the person to pick the group.</summary>
    Task<IReadOnlyList<TelegramChatView>> FindChatsAsync(string name, CancellationToken cancellationToken);

    /// <summary>Writes a short test message with the bot, where its routines would write.</summary>
    Task TestAsync(string name, CancellationToken cancellationToken);
}
