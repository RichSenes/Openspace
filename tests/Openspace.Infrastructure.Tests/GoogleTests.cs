using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Openspace.Application.Routines;
using Openspace.Infrastructure.Google;

namespace Openspace.Infrastructure.Tests;

/// <summary>Google accounts and Gmail against a made-up Google: no real account, no real key.</summary>
public sealed class GoogleTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"openspace-google-{Guid.NewGuid():N}");
    private readonly FakeGoogle _google = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero));
    private readonly GoogleOptions _options;

    public GoogleTests()
    {
        Directory.CreateDirectory(_folder);
        _options = new GoogleOptions
        {
            Folder = _folder,
            AuthUri = new Uri("https://auth.example/authorize"),
            TokenUri = new Uri("https://auth.example/token"),
            RevokeUri = new Uri("https://auth.example/revoke"),
            GmailUri = new Uri("https://mail.example/"),
            TasksUri = new Uri("https://tasks.example/"),
            CalendarUri = new Uri("https://calendar.example/"),
        };
        File.WriteAllText(Path.Combine(_folder, GoogleAccounts.ClientFile), """{"installed":{"client_id":"made-up-client.apps.example","client_secret":"made-up-secret"}}""");
    }

    public void Dispose()
    {
        _google.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private GoogleAccounts Accounts() => new(new HttpClient(_google), new LiveOptions<GoogleOptions>(_options), _clock);

    private static readonly Uri Callback = new("http://localhost:5173/api/google/callback");

    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

    private async Task<(GoogleAccounts Accounts, string Email)> ConnectedAsync()
    {
        var accounts = Accounts();
        var state = Query(accounts.BeginConnect(Callback))["state"];
        return (accounts, await accounts.FinishConnectAsync(state, "made-up-code", default));
    }

    [Fact]
    public void Connecting_sends_the_person_to_google_with_a_pkce_challenge_for_mail_calendar_and_tasks()
    {
        var url = Accounts().BeginConnect(Callback);

        var query = Query(url);
        Assert.StartsWith("https://auth.example/authorize?", url.ToString(), StringComparison.Ordinal);
        Assert.Equal("made-up-client.apps.example", query["client_id"]);
        Assert.Equal(Callback.ToString(), query["redirect_uri"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Contains("gmail.modify", query["scope"], StringComparison.Ordinal);
        Assert.Contains("calendar.events", query["scope"], StringComparison.Ordinal);
        Assert.Contains("auth/tasks", query["scope"], StringComparison.Ordinal);
        Assert.DoesNotContain("made-up-secret", url.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_this_computer_can_be_the_way_back_and_only_with_a_client()
    {
        Assert.Throws<InvalidOperationException>(() => Accounts().BeginConnect(new Uri("https://office.example/api/google/callback")));

        File.Delete(Path.Combine(_folder, GoogleAccounts.ClientFile));
        var missing = Assert.Throws<InvalidOperationException>(() => Accounts().BeginConnect(Callback));
        Assert.Contains("google-client.json", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_code_becomes_a_key_only_the_person_can_read_named_after_the_account()
    {
        var (accounts, email) = await ConnectedAsync();

        Assert.Equal("someone@example.com", email);
        var exchange = _google.Calls.Single(c => c.Path == "/token");
        Assert.Contains("grant_type=authorization_code", exchange.Body, StringComparison.Ordinal);
        Assert.Contains("code_verifier=", exchange.Body, StringComparison.Ordinal);
        Assert.Contains("redirect_uri=http://localhost:5173/api/google/callback", exchange.Body, StringComparison.Ordinal);

        var file = Path.Combine(_folder, "google", "someone@example.com.json");
        var saved = File.ReadAllText(file);
        Assert.Contains("made-up-refresh", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("made-up-access", saved, StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(file)!));
        }

        var status = await accounts.StatusAsync(default);
        Assert.True(status.ClientReady);
        var account = Assert.Single(status.Accounts);
        Assert.Equal("someone@example.com", account.Email);
        Assert.Contains("https://www.googleapis.com/auth/gmail.modify", account.Scopes);
    }

    [Fact]
    public async Task An_answer_is_taken_once_and_only_while_fresh()
    {
        var accounts = Accounts();
        var state = Query(accounts.BeginConnect(Callback))["state"];
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.FinishConnectAsync("made-up-state", "code", default));

        _clock.Advance(TimeSpan.FromMinutes(11));
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.FinishConnectAsync(state, "code", default));
        Assert.Empty((await accounts.StatusAsync(default)).Accounts);
    }

    [Fact]
    public async Task Tokens_are_kept_until_they_expire_then_made_again_from_the_key()
    {
        var (_, email) = await ConnectedAsync();
        var fresh = Accounts();

        Assert.Equal("made-up-access-2", await fresh.AccessTokenAsync(email, default));
        Assert.Equal("made-up-access-2", await fresh.AccessTokenAsync(email, default));
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal("made-up-access-3", await fresh.AccessTokenAsync(email, default));

        var refreshes = _google.Calls.Where(c => c.Body.Contains("grant_type=refresh_token", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, refreshes.Count);
        Assert.All(refreshes, r => Assert.Contains("refresh_token=made-up-refresh", r.Body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_revoked_key_asks_to_connect_again_without_showing_it()
    {
        var (_, email) = await ConnectedAsync();
        _google.RefuseRefresh = true;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Accounts().AccessTokenAsync(email, default));

        Assert.Contains("connect the account again", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("made-up", error.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Accounts().AccessTokenAsync("nobody@example.com", default));
    }

    [Fact]
    public async Task Disconnecting_gives_the_key_back_and_forgets_it()
    {
        var (accounts, email) = await ConnectedAsync();

        await accounts.DisconnectAsync(email, default);

        Assert.Contains(_google.Calls, c => c.Path == "/revoke" && c.Body.Contains("token=made-up-refresh", StringComparison.Ordinal));
        Assert.Empty((await accounts.StatusAsync(default)).Accounts);
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.AccessTokenAsync(email, default));
    }

    [Fact]
    public async Task Mail_is_read_with_its_headers_text_and_whether_it_went_to_a_list()
    {
        var (accounts, email) = await ConnectedAsync();
        var mailbox = new GmailMailbox(new HttpClient(_google), accounts, new LiveOptions<GoogleOptions>(_options));

        var first = await mailbox.FindAsync(email, "in:inbox newer_than:2d", null, default);
        var second = await mailbox.FindAsync(email, "in:inbox newer_than:2d", first.Next, default);
        var messages = new List<MailMessage>();
        foreach (var id in first.Ids.Concat(second.Ids))
        {
            messages.Add(await mailbox.ReadAsync(email, id, default));
        }

        Assert.Equal(["m1"], first.Ids);
        Assert.Equal("page-2", first.Next);
        Assert.Null(second.Next);

        Assert.Equal(2, messages.Count);
        var personal = messages[0];
        Assert.Equal("m1", personal.Id);
        Assert.Equal("t1", personal.ThreadId);
        Assert.Equal("Ada Example <ada@example.com>", personal.From);
        Assert.Equal("ada.replies@example.com", personal.ReplyTo);
        Assert.Equal("Dinner on Saturday?", personal.Subject);
        Assert.Equal("<abc@mail.example>", personal.MessageId);
        Assert.Equal("Shall we meet at eight?\nLet me know.", personal.Text);
        Assert.True(personal.Unread);
        Assert.False(personal.Bulk);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 18, 0, 0, TimeSpan.Zero), personal.Received);

        var letter = messages[1];
        Assert.True(letter.Bulk);
        Assert.Equal("Weekly news\nThree things & more.", letter.Text);
        Assert.Contains(_google.Calls, c => c.Path == "/gmail/v1/users/me/messages" && c.Query.Contains("q=in%3Ainbox%20newer_than%3A2d", StringComparison.Ordinal) && c.Query.Contains("pageToken=page-2", StringComparison.Ordinal));
        Assert.All(_google.Calls.Where(c => c.Path.StartsWith("/gmail/", StringComparison.Ordinal)), c => Assert.Equal("Bearer made-up-access", c.Authorization));
    }

    [Fact]
    public async Task A_conversation_is_read_whole_oldest_first_telling_the_person_s_own_mail()
    {
        var (accounts, email) = await ConnectedAsync();
        var mailbox = new GmailMailbox(new HttpClient(_google), accounts, new LiveOptions<GoogleOptions>(_options));

        var thread = await mailbox.ReadThreadAsync(email, "t1", default);

        Assert.Equal(["m1", "m3"], thread.Select(m => m.Id));
        Assert.False(thread[0].Sent);
        Assert.True(thread[1].Sent);
        Assert.Equal("Ada Example <ada@example.com>", thread[1].To);
        Assert.Contains(_google.Calls, c => c.Path == "/gmail/v1/users/me/threads/t1" && c.Query == "?format=full");
    }

    [Fact]
    public async Task A_label_is_made_once_and_archiving_takes_the_message_out_of_the_inbox()
    {
        var (accounts, email) = await ConnectedAsync();
        var mailbox = new GmailMailbox(new HttpClient(_google), accounts, new LiveOptions<GoogleOptions>(_options));

        await mailbox.LabelAsync(email, "m2", "Openspace/Newsletters", archive: true, default);
        await mailbox.LabelAsync(email, "m3", "Openspace/Newsletters", archive: false, default);
        await mailbox.LabelAsync(email, "m4", "Existing", archive: false, default);

        Assert.Single(_google.Calls, c => c.Path == "/gmail/v1/users/me/labels" && c.Method == "POST");
        var modified = _google.Calls.Where(c => c.Path.EndsWith("/modify", StringComparison.Ordinal)).ToList();
        Assert.Equal("""{"addLabelIds":["Label_new"],"removeLabelIds":["INBOX"]}""", modified[0].Body);
        Assert.Equal("""{"addLabelIds":["Label_new"],"removeLabelIds":[]}""", modified[1].Body);
        Assert.Equal("""{"addLabelIds":["Label_1"],"removeLabelIds":[]}""", modified[2].Body);
    }

    [Fact]
    public async Task A_reply_becomes_a_draft_in_the_same_conversation_and_is_never_sent()
    {
        var (accounts, email) = await ConnectedAsync();
        var mailbox = new GmailMailbox(new HttpClient(_google), accounts, new LiveOptions<GoogleOptions>(_options));

        var id = await mailbox.DraftReplyAsync(email, new MailReply("t1", "ada@example.com", "Re: Dinner on Saturday?", "<abc@mail.example>", "Sì, alle otto va benissimo."), default);

        Assert.Equal("draft-1", id);
        var call = Assert.Single(_google.Calls, c => c.Path == "/gmail/v1/users/me/drafts");
        using var body = JsonDocument.Parse(call.Body);
        Assert.Equal("t1", body.RootElement.GetProperty("message").GetProperty("threadId").GetString());
        var raw = body.RootElement.GetProperty("message").GetProperty("raw").GetString()!.Replace('-', '+').Replace('_', '/');
        var mail = Encoding.UTF8.GetString(Convert.FromBase64String(raw.PadRight(raw.Length + ((4 - (raw.Length % 4)) % 4), '=')));
        Assert.Contains("To: ada@example.com\r\n", mail, StringComparison.Ordinal);
        Assert.Contains("In-Reply-To: <abc@mail.example>\r\n", mail, StringComparison.Ordinal);
        Assert.Contains("charset=UTF-8", mail, StringComparison.Ordinal);
        Assert.DoesNotContain(_google.Calls, c => c.Path.Contains("send", StringComparison.Ordinal));
    }

    [Fact]
    public void Headers_cannot_be_split_by_a_line_break_in_a_field()
    {
        var mail = GmailMailbox.Rfc822(new MailReply("t1", "ada@example.com\r\nBcc: someone@example.com", "Hi", "", "Body"));

        Assert.DoesNotContain("\r\nBcc:", mail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_task_goes_to_the_default_list_due_that_day_with_its_place_in_the_notes()
    {
        var (accounts, email) = await ConnectedAsync();
        var agenda = new GoogleAgenda(new HttpClient(_google), accounts, new LiveOptions<GoogleOptions>(_options));

        var link = await agenda.AddAsync(email, new AgendaItem(false, "Pay the electricity bill", new DateOnly(2026, 3, 10), null, 60, "Online", "From the bill"), TimeZoneInfo.Utc, default);

        Assert.Equal("https://tasks.example/task-1", link);
        var call = Assert.Single(_google.Calls, c => c.Path == "/tasks/v1/lists/@default/tasks");
        Assert.Equal("""{"title":"Pay the electricity bill","notes":"Online\nFrom the bill","due":"2026-03-10T00:00:00.000Z"}""", call.Body);
        Assert.Equal("Bearer made-up-access", call.Authorization);
    }

    [Fact]
    public async Task An_event_goes_to_the_main_calendar_on_the_person_s_clock_without_guests_or_emails()
    {
        var (accounts, email) = await ConnectedAsync();
        var agenda = new GoogleAgenda(new HttpClient(_google), accounts, new LiveOptions<GoogleOptions>(_options));
        var rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

        var link = await agenda.AddAsync(email, new AgendaItem(true, "Dentist", new DateOnly(2026, 3, 12), new TimeOnly(10, 30), 45, "Clinic", "From the clinic"), rome, default);
        await agenda.AddAsync(email, new AgendaItem(true, "Town fair", new DateOnly(2026, 3, 14), null, 60, "", "From the town"), rome, default);

        Assert.Equal("https://calendar.example/event-1", link);
        var calls = _google.Calls.Where(c => c.Path == "/calendar/v3/calendars/primary/events").ToList();
        Assert.All(calls, c => Assert.Equal("?sendUpdates=none", c.Query));
        Assert.All(calls, c => Assert.DoesNotContain("attendees", c.Body, StringComparison.Ordinal));
        using var timed = JsonDocument.Parse(calls[0].Body);
        Assert.Equal("2026-03-12T10:30:00", timed.RootElement.GetProperty("start").GetProperty("dateTime").GetString());
        Assert.Equal("2026-03-12T11:15:00", timed.RootElement.GetProperty("end").GetProperty("dateTime").GetString());
        Assert.Equal("Europe/Rome", timed.RootElement.GetProperty("start").GetProperty("timeZone").GetString());
        using var allDay = JsonDocument.Parse(calls[1].Body);
        Assert.Equal("2026-03-14", allDay.RootElement.GetProperty("start").GetProperty("date").GetString());
        Assert.Equal("2026-03-15", allDay.RootElement.GetProperty("end").GetProperty("date").GetString());
    }

    private static string Base64Url(string text) => GoogleAccounts.Base64Url(Encoding.UTF8.GetBytes(text));

    /// <summary>Google's sign-in and Gmail, made up: answers by path, records every call.</summary>
    private sealed class FakeGoogle : HttpMessageHandler
    {
        private int _accessTokens;

        public List<(string Method, string Path, string Query, string Body, string? Authorization)> Calls { get; } = [];

        public bool RefuseRefresh { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var form = request.Content is FormUrlEncodedContent ? Uri.UnescapeDataString(body) : body;
            Calls.Add((request.Method.Method, uri.AbsolutePath, uri.Query, form, request.Headers.Authorization?.ToString()));
            return (uri.AbsolutePath, request.Method.Method) switch
            {
                ("/token", _) when RefuseRefresh && form.Contains("refresh_token", StringComparison.Ordinal) => Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest),
                ("/token", _) => Json(Tokens(form.Contains("authorization_code", StringComparison.Ordinal))),
                ("/revoke", _) => Json("{}"),
                ("/gmail/v1/users/me/profile", _) => Json("""{"emailAddress":"someone@example.com"}"""),
                ("/gmail/v1/users/me/messages", _) when uri.Query.Contains("pageToken=page-2", StringComparison.Ordinal) => Json("""{"messages":[{"id":"m2","threadId":"t2"}]}"""),
                ("/gmail/v1/users/me/messages", _) => Json("""{"messages":[{"id":"m1","threadId":"t1"}],"nextPageToken":"page-2","resultSizeEstimate":2}"""),
                ("/gmail/v1/users/me/messages/m1", _) => Json(Personal()),
                ("/gmail/v1/users/me/messages/m2", _) => Json(Letter()),
                ("/gmail/v1/users/me/threads/t1", _) => Json($"{{\"id\":\"t1\",\"messages\":[{Answer()},{Personal()}]}}"),
                ("/gmail/v1/users/me/labels", "GET") => Json("""{"labels":[{"id":"INBOX","name":"INBOX"},{"id":"Label_1","name":"Existing"}]}"""),
                ("/gmail/v1/users/me/labels", "POST") => Json("""{"id":"Label_new","name":"Openspace/Newsletters"}"""),
                ("/gmail/v1/users/me/drafts", _) => Json("""{"id":"draft-1"}"""),
                ("/tasks/v1/lists/@default/tasks", "POST") => Json("""{"id":"task-1","webViewLink":"https://tasks.example/task-1"}"""),
                ("/calendar/v3/calendars/primary/events", "POST") => Json("""{"id":"event-1","htmlLink":"https://calendar.example/event-1"}"""),
                _ when uri.AbsolutePath.EndsWith("/modify", StringComparison.Ordinal) => Json("{}"),
                _ => Json("""{"error":{"message":"Not found"}}""", HttpStatusCode.NotFound),
            };
        }

        private string Tokens(bool first)
        {
            var access = first ? "made-up-access" : $"made-up-access-{++_accessTokens + 1}";
            return first
                ? $$"""{"access_token":"{{access}}","refresh_token":"made-up-refresh","expires_in":3599,"scope":"https://www.googleapis.com/auth/gmail.modify https://www.googleapis.com/auth/tasks"}"""
                : $$"""{"access_token":"{{access}}","expires_in":3599}""";
        }

        private static string Personal() => $$$"""
            {"id":"m1","threadId":"t1","labelIds":["INBOX","UNREAD"],"snippet":"Shall we meet at eight?","internalDate":"1772388000000",
             "payload":{"mimeType":"multipart/alternative","headers":[
               {"name":"From","value":"Ada Example <ada@example.com>"},{"name":"Reply-To","value":"ada.replies@example.com"},
               {"name":"Subject","value":"Dinner on Saturday?"},{"name":"Message-ID","value":"<abc@mail.example>"}],
              "parts":[{"mimeType":"text/plain","body":{"data":"{{{Base64Url("Shall we meet at eight?\nLet me know.")}}}"}},
                       {"mimeType":"text/html","body":{"data":"{{{Base64Url("<p>Shall we meet at eight?</p>")}}}"} }]} }
            """;

        /// <summary>The person's own answer to m1, a day later.</summary>
        private static string Answer() => $$$"""
            {"id":"m3","threadId":"t1","labelIds":["SENT"],"snippet":"Eight is fine.","internalDate":"1772474400000",
             "payload":{"mimeType":"text/plain","headers":[{"name":"From","value":"Someone <someone@example.com>"},{"name":"To","value":"Ada Example <ada@example.com>"},
               {"name":"Subject","value":"Re: Dinner on Saturday?"}],
              "body":{"data":"{{{Base64Url("Eight is fine.")}}}"} } }
            """;

        private static string Letter() => $$$"""
            {"id":"m2","threadId":"t2","labelIds":["INBOX"],"snippet":"Weekly news","internalDate":"1772388000000",
             "payload":{"mimeType":"text/html","headers":[{"name":"From","value":"News <news@letters.example>"},{"name":"Subject","value":"This week"},
               {"name":"List-Unsubscribe","value":"<https://letters.example/unsubscribe>"}],
              "body":{"data":"{{{Base64Url("<html><head><style>p{color:red}</style></head><body><h1>Weekly news</h1><p>Three things &amp; more.</p></body></html>")}}}"} } }
            """;

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
