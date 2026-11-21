namespace Openspace.Application.Google;

/// <param name="Scopes">What the person allowed: mail, calendar, tasks.</param>
public sealed record GoogleAccountView(string Email, DateTimeOffset ConnectedAt, IReadOnlyList<string> Scopes);

/// <param name="ClientReady">Whether the person's OAuth client is in <paramref name="Folder"/>: without it no account can be connected.</param>
public sealed record GoogleStatusView(bool ClientReady, string Folder, IReadOnlyList<GoogleAccountView> Accounts);

/// <summary>
/// The person's Google accounts, connected with their own OAuth client. Each account's key
/// stays in a file only the person can read, on this machine: never in the database, never
/// in a repository. Routines name the account they work on by its address.
/// </summary>
public interface IGoogleAccounts
{
    Task<GoogleStatusView> StatusAsync(CancellationToken cancellationToken);

    /// <summary>Where to send the person to say yes; Google sends them back to <paramref name="callback"/>, on this machine.</summary>
    Uri BeginConnect(Uri callback);

    /// <summary>The person said yes: keeps the account's key.</summary>
    /// <returns>The account's address.</returns>
    Task<string> FinishConnectAsync(string state, string code, CancellationToken cancellationToken);

    /// <summary>Gives the key back to Google and forgets it.</summary>
    Task DisconnectAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps the OAuth client the person downloaded from Google Cloud, after checking it is one;
    /// it replaces the one there was. Its secret is never shown again.
    /// </summary>
    Task SaveClientAsync(string json, CancellationToken cancellationToken);

    /// <summary>Asks Google for a fresh token for the account, so the person knows its key still works.</summary>
    Task CheckAsync(string email, CancellationToken cancellationToken);

    /// <summary>A short-lived token to call Google for this account.</summary>
    Task<string> AccessTokenAsync(string email, CancellationToken cancellationToken);
}
