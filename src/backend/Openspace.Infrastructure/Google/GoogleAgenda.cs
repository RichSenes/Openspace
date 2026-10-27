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
/// their main calendar, never with guests, so nobody gets an invitation or an email.
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

    /// <summary>A task has no place of its own: it goes in the notes.</summary>
    private static string Notes(AgendaItem item) => string.IsNullOrWhiteSpace(item.Place) ? item.Notes : $"{item.Place}\n{item.Notes}";

    /// <summary>Calendar takes time zones by their IANA name ("Europe/Rome"), which Windows names otherwise.</summary>
    internal static string IanaName(TimeZoneInfo zone) =>
        zone.HasIanaId ? zone.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : "UTC";

    private async Task<JsonElement> PostAsync(string account, Uri uri, string service, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accounts.AccessTokenAsync(account, cancellationToken).ConfigureAwait(false));
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await GoogleAccounts.EnsureAsync(response, service, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).RootElement.Clone();
    }
}
