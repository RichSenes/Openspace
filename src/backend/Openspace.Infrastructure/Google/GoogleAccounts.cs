using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Openspace.Application.Google;

namespace Openspace.Infrastructure.Google;

internal static class GoogleHttp
{
    public const string Name = "google";
}

public sealed class GoogleOptions
{
    public const string SectionName = "Google";

    /// <summary>
    /// The person's own folder: <c>google-client.json</c>, the OAuth client they made in their
    /// Google Cloud project (a desktop app), and <c>google/</c>, one key per connected account.
    /// Never in a repository.
    /// </summary>
    public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "openspace");

    public Uri AuthUri { get; set; } = new("https://accounts.google.com/o/oauth2/v2/auth");

    public Uri TokenUri { get; set; } = new("https://oauth2.googleapis.com/token");

    public Uri RevokeUri { get; set; } = new("https://oauth2.googleapis.com/revoke");

    public Uri GmailUri { get; set; } = new("https://gmail.googleapis.com/");

    public Uri TasksUri { get; set; } = new("https://tasks.googleapis.com/");

    public Uri CalendarUri { get; set; } = new("https://www.googleapis.com/");
}

/// <summary>
/// Google accounts connected through the person's own OAuth client, the way desktop apps do
/// it: the browser goes to Google, Google comes back to this machine with a code, the code
/// becomes a key that lasts. Keys are files only the person can read; the short-lived tokens
/// made from them stay in memory.
/// </summary>
internal sealed class GoogleAccounts(HttpClient http, IOptionsMonitor<GoogleOptions> options, TimeProvider clock) : IGoogleAccounts, IDisposable
{
    public const string ClientFile = "google-client.json";

    /// <summary>Mail to read, label and draft; calendar events and tasks for the deadlines.</summary>
    public static readonly string[] Scopes =
    [
        "https://www.googleapis.com/auth/gmail.modify",
        "https://www.googleapis.com/auth/calendar.events",
        "https://www.googleapis.com/auth/tasks",
    ];

    private static readonly TimeSpan ConnectWindow = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Until)> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshing = new(1, 1);

    private string Folder => options.CurrentValue.Folder;

    private string AccountsFolder => Path.Combine(Folder, "google");

    public void Dispose() => _refreshing.Dispose();

    public Task<GoogleStatusView> StatusAsync(CancellationToken cancellationToken)
    {
        var accounts = Directory.Exists(AccountsFolder)
            ? Directory.GetFiles(AccountsFolder, "*.json")
                .Select(ReadKey)
                .Where(k => k is not null)
                .Select(k => new GoogleAccountView(k!.Email, k.ConnectedAt, k.Scopes))
                .OrderBy(a => a.Email, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
        return Task.FromResult(new GoogleStatusView(File.Exists(Path.Combine(Folder, ClientFile)), Folder, accounts));
    }

    public Uri BeginConnect(Uri callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!callback.IsLoopback)
        {
            throw new InvalidOperationException("Google accounts can be connected only from this computer: open Openspace on localhost.");
        }

        var client = ReadClient();
        var now = clock.GetUtcNow();
        foreach (var old in _pending.Where(p => p.Value.Until < now).ToList())
        {
            _pending.TryRemove(old.Key, out _);
        }

        var state = Random(24);
        var verifier = Random(48);
        _pending[state] = new Pending(verifier, callback, now + ConnectWindow);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = new Dictionary<string, string>
        {
            ["client_id"] = client.Id,
            ["redirect_uri"] = callback.ToString(),
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', Scopes),
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        return new Uri($"{options.CurrentValue.AuthUri}?{string.Join('&', query.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}"))}");
    }

    public async Task<string> FinishConnectAsync(string state, string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(state) || !_pending.TryRemove(state, out var pending) || pending.Until < clock.GetUtcNow())
        {
            throw new InvalidOperationException("This answer from Google is unknown or too old: start again from Settings.");
        }

        var client = ReadClient();
        var tokens = await PostTokenAsync(
            new Dictionary<string, string>
            {
                ["client_id"] = client.Id,
                ["client_secret"] = client.Secret,
                ["code"] = code,
                ["code_verifier"] = pending.Verifier,
                ["redirect_uri"] = pending.Callback.ToString(),
                ["grant_type"] = "authorization_code",
            },
            cancellationToken).ConfigureAwait(false);
        var refresh = tokens.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (string.IsNullOrEmpty(refresh))
        {
            throw new InvalidOperationException("Google gave no lasting key: remove Openspace from the account's third-party access and connect it again.");
        }

        var access = tokens.GetProperty("access_token").GetString()!;
        var scopes = tokens.TryGetProperty("scope", out var s) ? s.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
        using var profile = new HttpRequestMessage(HttpMethod.Get, new Uri(options.CurrentValue.GmailUri, "gmail/v1/users/me/profile"));
        profile.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var answer = await http.SendAsync(profile, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(answer, "Gmail", cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var email = document.RootElement.GetProperty("emailAddress").GetString()!;

        WriteKey(new AccountKey(email, refresh, scopes, clock.GetUtcNow()));
        _tokens[email] = (access, clock.GetUtcNow() + ExpiresIn(tokens));
        return email;
    }

    public async Task DisconnectAsync(string email, CancellationToken cancellationToken)
    {
        var file = KeyFile(email);
        if (ReadKey(file) is { } key)
        {
            // Giving the key back is a courtesy: forgetting it is what matters, so a refusal does not stop it.
            using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = key.RefreshToken });
            try
            {
                using var _ = await http.PostAsync(options.CurrentValue.RevokeUri, form, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
            }
        }

        File.Delete(file);
        _tokens.TryRemove(email, out _);
    }

    public async Task<string> AccessTokenAsync(string email, CancellationToken cancellationToken)
    {
        if (_tokens.TryGetValue(email, out var cached) && cached.Until > clock.GetUtcNow())
        {
            return cached.Token;
        }

        await _refreshing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_tokens.TryGetValue(email, out cached) && cached.Until > clock.GetUtcNow())
            {
                return cached.Token;
            }

            var key = ReadKey(KeyFile(email)) ?? throw new InvalidOperationException($"The Google account {email} is not connected: connect it in Settings.");
            var client = ReadClient();
            var tokens = await PostTokenAsync(
                new Dictionary<string, string>
                {
                    ["client_id"] = client.Id,
                    ["client_secret"] = client.Secret,
                    ["refresh_token"] = key.RefreshToken,
                    ["grant_type"] = "refresh_token",
                },
                cancellationToken).ConfigureAwait(false);
            var access = tokens.GetProperty("access_token").GetString()!;
            _tokens[email] = (access, clock.GetUtcNow() + ExpiresIn(tokens));
            return access;
        }
        finally
        {
            _refreshing.Release();
        }
    }

    /// <summary>A minute early, so a token never runs out halfway through a call.</summary>
    private static TimeSpan ExpiresIn(JsonElement tokens) =>
        TimeSpan.FromSeconds((tokens.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : 3600) - 60);

    private async Task<JsonElement> PostTokenAsync(Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(fields);
        using var response = await http.PostAsync(options.CurrentValue.TokenUri, form, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Google says why with a code ("invalid_grant": the key was revoked or expired); the keys never go into the message.
            var reason = TryRead(body, "error");
            throw new InvalidOperationException(reason == "invalid_grant"
                ? "Google no longer accepts this account's key: connect the account again in Settings."
                : $"Google refused the sign-in ({reason ?? response.StatusCode.ToString()}).");
        }

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    internal static async Task EnsureAsync(HttpResponseMessage response, string service, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string? message = null;
            try
            {
                using var document = JsonDocument.Parse(body);
                message = document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var m) ? m.GetString() : null;
            }
            catch (JsonException)
            {
            }

            throw new HttpRequestException($"{service} refused the request: {message ?? response.StatusCode.ToString()}");
        }
    }

    private static string? TryRead(string json, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(property, out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The OAuth client as Google gives it to download: <c>{"installed": {"client_id", "client_secret"}}</c>.</summary>
    private (string Id, string Secret) ReadClient()
    {
        var file = Path.Combine(Folder, ClientFile);
        if (!File.Exists(file))
        {
            throw new InvalidOperationException($"No Google OAuth client: put the one of your Google Cloud project (a desktop app) in {file}.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(file));
        var root = document.RootElement;
        var client = root.TryGetProperty("installed", out var installed) ? installed : root.TryGetProperty("web", out var web) ? web : root;
        return (client.GetProperty("client_id").GetString()!, client.TryGetProperty("client_secret", out var secret) ? secret.GetString() ?? string.Empty : string.Empty);
    }

    private string KeyFile(string email)
    {
        var name = new string([.. email.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is '@' or '.' or '-' or '_' or '+' ? c : '_')]);
        if (name.Length == 0 || name.Trim('.').Length == 0)
        {
            throw new InvalidOperationException("Which Google account?");
        }

        return Path.Combine(AccountsFolder, name + ".json");
    }

    private static AccountKey? ReadKey(string file)
    {
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AccountKey>(File.ReadAllText(file), JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void WriteKey(AccountKey key)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(AccountsFolder);
        }
        else
        {
            Directory.CreateDirectory(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.CreateDirectory(AccountsFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var file = KeyFile(key.Email);
        var temporary = file + ".new";
        var stream = OperatingSystem.IsWindows()
            ? new FileStream(temporary, FileMode.Create, FileAccess.Write)
            : new FileStream(temporary, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        using (stream)
        {
            JsonSerializer.Serialize(stream, key, JsonSerializerOptions.Web);
        }

        File.Move(temporary, file, overwrite: true);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static string Random(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record Pending(string Verifier, Uri Callback, DateTimeOffset Until);

    private sealed record AccountKey(string Email, string RefreshToken, string[] Scopes, DateTimeOffset ConnectedAt);
}
