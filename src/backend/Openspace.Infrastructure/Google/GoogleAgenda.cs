using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Openspace.Application.Google;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Google;

/// <summary>
/// Google Tasks and Calendar as the person's agenda: tasks go to their default list, events to
/// their main calendar, never with guests, so nobody gets an invitation or an email. Reading
/// looks at the same two places.
/// </summary>
internal sealed class GoogleAgenda(HttpClient http, IGoogleAccounts accounts, IOptionsMonitor<GoogleOptions> options) : IAgenda
{
    public async Task<string> AddAsync(string account, AgendaItem item, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(zone);
        if (!item.IsEvent)
        {
            var task = await PostAsync(
                account, new Uri(options.CurrentValue.TasksUri, "tasks/v1/lists/@default/tasks"), "Google Tasks",
                new { title = item.Title, notes = Notes(item), due = $"{item.Date:yyyy-MM-dd}T00:00:00.000Z" }, cancellationToken).ConfigureAwait(false);
            return task.TryGetProperty("webViewLink", out var link) ? link.GetString()! : task.GetProperty("id").GetString()!;
        }

        object start, end;
        if (item.Time is { } time)
        {
            var name = IanaName(zone);
            var from = item.Date.ToDateTime(time);
            start = new { dateTime = from.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), timeZone = name };
            end = new { dateTime = from.AddMinutes(Math.Max(item.Minutes, 15)).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), timeZone = name };
        }
        else
        {
            start = new { date = item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            end = new { date = item.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        }

        var created = await PostAsync(
            account, new Uri(options.CurrentValue.CalendarUri, "calendar/v3/calendars/primary/events?sendUpdates=none"), "Google Calendar",
            new { summary = item.Title, description = item.Notes, location = item.Place, start, end }, cancellationToken).ConfigureAwait(false);
        return created.TryGetProperty("htmlLink", out var html) ? html.GetString()! : created.GetProperty("id").GetString()!;
    }

    public async Task<IReadOnlyList<AgendaEntry>> ReadAsync(string account, DateOnly from, DateOnly until, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zone);
        string Instant(DateOnly day) =>
            new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue))).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

        var entries = new List<AgendaEntry>();
        var events = await GetAsync(
            account,
            new Uri(options.CurrentValue.CalendarUri,
                $"calendar/v3/calendars/primary/events?singleEvents=true&orderBy=startTime&maxResults=250&timeMin={Uri.EscapeDataString(Instant(from))}&timeMax={Uri.EscapeDataString(Instant(until))}"),
            "Google Calendar", cancellationToken).ConfigureAwait(false);
        foreach (var item in Items(events))
        {
            if ((item.TryGetProperty("status", out var status) && status.GetString() == "cancelled") || !item.TryGetProperty("start", out var start))
            {
                continue;
            }

            var title = item.TryGetProperty("summary", out var summary) ? summary.GetString() ?? string.Empty : string.Empty;
            var place = item.TryGetProperty("location", out var location) ? location.GetString() ?? string.Empty : string.Empty;
            if (start.TryGetProperty("dateTime", out var at) && DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var when))
            {
                var local = TimeZoneInfo.ConvertTime(when, zone);
                entries.Add(new AgendaEntry(true, title, DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime), place));
            }
            else if (start.TryGetProperty("date", out var day) && DateOnly.TryParseExact(day.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                entries.Add(new AgendaEntry(true, title, date, null, place));
            }
        }

        var tasks = await GetAsync(
            account,
            new Uri(options.CurrentValue.TasksUri,
                $"tasks/v1/lists/@default/tasks?showCompleted=false&maxResults=100&dueMax={Uri.EscapeDataString($"{until:yyyy-MM-dd}T00:00:00.000Z")}"),
            "Google Tasks", cancellationToken).ConfigureAwait(false);
        foreach (var task in Items(tasks))
        {
            // Google keeps only the day of a task's due date, written as midnight UTC.
            if (task.TryGetProperty("due", out var due) && due.GetString() is { Length: >= 10 } text
                && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date < until
                && !(task.TryGetProperty("status", out var status) && status.GetString() == "completed"))
            {
                entries.Add(new AgendaEntry(false, task.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty, date, null, string.Empty));
            }
        }

        return [.. entries.Where(e => !string.IsNullOrWhiteSpace(e.Title)).OrderBy(e => e.Date).ThenByDescending(e => e.IsEvent).ThenBy(e => e.Time)];
    }

    private static List<JsonElement> Items(JsonElement list) =>
        list.ValueKind == JsonValueKind.Object && list.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array ? [.. items.EnumerateArray()] : [];

    /// <summary>A task has no place of its own: it goes in the notes.</summary>
    private static string Notes(AgendaItem item) => string.IsNullOrWhiteSpace(item.Place) ? item.Notes : $"{item.Place}\n{item.Notes}";

    /// <summary>Calendar takes time zones by their IANA name ("Europe/Rome"), which Windows names otherwise.</summary>
    internal static string IanaName(TimeZoneInfo zone) =>
        zone.HasIanaId ? zone.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : "UTC";

    private async Task<JsonElement> GetAsync(string account, Uri uri, string service, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accounts.AccessTokenAsync(account, cancellationToken).ConfigureAwait(false));
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await GoogleAccounts.EnsureAsync(response, service, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).RootElement.Clone();
    }

    private async Task<JsonElement> PostAsync(string account, Uri uri, string service, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accounts.AccessTokenAsync(account, cancellationToken).ConfigureAwait(false));
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await GoogleAccounts.EnsureAsync(response, service, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).RootElement.Clone();
    }
}
