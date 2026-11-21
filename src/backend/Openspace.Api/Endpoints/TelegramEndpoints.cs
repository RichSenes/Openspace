using Openspace.Application.Telegram;

namespace Openspace.Api.Endpoints;

/// <summary>Setting up the person's Telegram: bots, the group and each bot's topic. Tokens go in, never out.</summary>
internal static class TelegramEndpoints
{
    /// <param name="Topic">The topic of the group the bot writes in; null for the main chat.</param>
    public sealed record BotRequest(string Name, string Token, long? Topic);

    public sealed record ChatRequest(long Chat);

    public sealed record TopicRequest(long? Topic);

    public static void MapTelegramEndpoints(this IEndpointRouteBuilder app)
    {
        var telegram = app.MapGroup("/api/telegram").WithTags("Telegram");
        telegram.MapGet("/", (ITelegramSetup setup, CancellationToken ct) => setup.StatusAsync(ct));

        telegram.MapPost("/bots", async (BotRequest request, ITelegramSetup setup, CancellationToken ct) =>
            Results.Ok(new { username = await setup.AddBotAsync(request.Name, request.Token, request.Topic, ct) }));

        telegram.MapDelete("/bots/{name}", async (string name, ITelegramSetup setup, CancellationToken ct) =>
        {
            await setup.RemoveBotAsync(name, ct);
            return Results.NoContent();
        });

        telegram.MapPut("/bots/{name}/topic", async (string name, TopicRequest request, ITelegramSetup setup, CancellationToken ct) =>
        {
            await setup.SetTopicAsync(name, request.Topic, ct);
            return Results.NoContent();
        });

        telegram.MapGet("/bots/{name}/chats", (string name, ITelegramSetup setup, CancellationToken ct) => setup.FindChatsAsync(name, ct));

        telegram.MapPost("/bots/{name}/test", async (string name, ITelegramSetup setup, CancellationToken ct) =>
        {
            await setup.TestAsync(name, ct);
            return Results.NoContent();
        });

        telegram.MapPut("/chat", async (ChatRequest request, ITelegramSetup setup, CancellationToken ct) =>
        {
            await setup.SetChatAsync(request.Chat, ct);
            return Results.NoContent();
        });
    }
}
