using Microsoft.Extensions.Options;
using Openspace.Application.Agents;
using Openspace.Application.Work;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Api.Endpoints;

internal static class TaskEndpoints
{
    /// <summary>With a desk the task is queued at once; without one it is proposed.</summary>
    public sealed record CreateTaskRequest(Guid OfficeId, Guid? DeskId, string Title, string? Description);

    public sealed record ApproveRequest(Guid? DeskId);

    public sealed record TextRequest(string Text);

    public sealed record PermissionRequest(bool Allow);

    public sealed record MergeRequest(string Target);

    public static void MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var tasks = app.MapGroup("/api/tasks").WithTags("Tasks");

        tasks.MapGet("/", (Guid? officeId, WorkTaskStatus? status, WorkTaskQueries queries, CancellationToken ct) =>
            queries.ListAsync(officeId is { } o ? new OfficeId(o) : null, status, ct));

        tasks.MapGet("/{taskId:guid}", (Guid taskId, WorkTaskQueries queries, CancellationToken ct) =>
            queries.GetAsync(new WorkTaskId(taskId), ct));

        tasks.MapGet("/{taskId:guid}/diff", async (Guid taskId, WorkTaskQueries queries, CancellationToken ct) =>
            Results.Text(await queries.DiffAsync(new WorkTaskId(taskId), ct), "text/plain"));

        tasks.MapPost("/{taskId:guid}/permission", (
            Guid taskId, PermissionRequest request, WorkTaskCommands commands, CancellationToken ct) =>
            NoContent(commands.ReplyToPermissionAsync(new WorkTaskId(taskId), request.Allow, ct)));

        tasks.MapPost("/{taskId:guid}/merge", (
            Guid taskId, MergeRequest request, WorkTaskCommands commands, CancellationToken ct) =>
            NoContent(commands.MergeAsync(new WorkTaskId(taskId), request.Target, ct)));

        app.MapGet("/api/activity", (Guid? officeId, ActivityBoard board) => board.Snapshot(officeId)).WithTags("Activity");

        tasks.MapPost("/", async (
            CreateTaskRequest request,
            WorkTaskCommands commands,
            IOptions<OperatorOptions> me,
            CancellationToken ct) =>
        {
            var office = new OfficeId(request.OfficeId);
            var id = request.DeskId is { } desk
                ? await commands.AssignAsync(office, new DeskId(desk), request.Title, request.Description, me.Value.Name, ct)
                : await commands.ProposeAsync(office, request.Title, request.Description, me.Value.Name, ct);
            return Results.Created($"/api/tasks/{id.Value}", new { id = id.Value });
        });

        tasks.MapPost("/{taskId:guid}/approve", async (
            Guid taskId, ApproveRequest request, WorkTaskCommands commands, CancellationToken ct) =>
        {
            await commands.ApproveAsync(new WorkTaskId(taskId), request.DeskId is { } d ? new DeskId(d) : null, ct);
            return Results.NoContent();
        });

        tasks.MapPost("/{taskId:guid}/accept", (Guid taskId, WorkTaskCommands commands, CancellationToken ct) =>
            NoContent(commands.AcceptAsync(new WorkTaskId(taskId), ct)));

        tasks.MapPost("/{taskId:guid}/retry", (Guid taskId, WorkTaskCommands commands, CancellationToken ct) =>
            NoContent(commands.RetryAsync(new WorkTaskId(taskId), ct)));

        tasks.MapPost("/{taskId:guid}/discard", (Guid taskId, WorkTaskCommands commands, CancellationToken ct) =>
            NoContent(commands.DiscardAsync(new WorkTaskId(taskId), ct)));

        tasks.MapPost("/{taskId:guid}/send-back", (
            Guid taskId, TextRequest request, WorkTaskCommands commands, IOptions<OperatorOptions> me, CancellationToken ct) =>
            NoContent(commands.SendBackAsync(new WorkTaskId(taskId), me.Value.Name, request.Text, ct)));

        tasks.MapPost("/{taskId:guid}/notes", (
            Guid taskId, TextRequest request, WorkTaskCommands commands, IOptions<OperatorOptions> me, CancellationToken ct) =>
            NoContent(commands.AddNoteAsync(new WorkTaskId(taskId), me.Value.Name, request.Text, ct)));
    }

    private static async Task<IResult> NoContent(Task action)
    {
        await action;
        return Results.NoContent();
    }
}
