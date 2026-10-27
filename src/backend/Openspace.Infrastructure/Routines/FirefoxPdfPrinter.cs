using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Routines;

public sealed class PdfOptions
{
    public const string SectionName = "Pdf";

    /// <summary>The WebDriver for Firefox: "geckodriver", or the snap's "firefox.geckodriver".</summary>
    public string Geckodriver { get; set; } = File.Exists("/snap/bin/firefox.geckodriver") ? "/snap/bin/firefox.geckodriver" : "geckodriver";
}

/// <summary>
/// Prints a page to PDF with Firefox, headless, through WebDriver's print command: an A4
/// page as the browser lays it out, background colours included.
/// </summary>
internal sealed class FirefoxPdfPrinter(IHttpClientFactory httpClients, IOptionsMonitor<PdfOptions> options) : IPdfPrinter, IDisposable
{
    public const string HttpClientName = "webdriver";

    private readonly SemaphoreSlim _one = new(1, 1);

    public async Task<byte[]> PrintAsync(string html, CancellationToken cancellationToken)
    {
        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        var port = FreePort();
        using var driver = Process.Start(new ProcessStartInfo(options.CurrentValue.Geckodriver)
        {
            ArgumentList = { "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Could not start geckodriver.");
        driver.OutputDataReceived += (_, _) => { };
        driver.ErrorDataReceived += (_, _) => { };
        driver.BeginOutputReadLine();
        driver.BeginErrorReadLine();

        try
        {
            using var http = httpClients.CreateClient(HttpClientName);
            http.BaseAddress = new Uri($"http://127.0.0.1:{port}/");
            var session = await StartAsync(http, cancellationToken).ConfigureAwait(false);
            try
            {
                var page = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
                await CallAsync(http, $"session/{session}/url", new { url = page }, cancellationToken).ConfigureAwait(false);
                var printed = await CallAsync(http, $"session/{session}/print", new
                {
                    background = true,
                    shrinkToFit = true,
                    page = new { width = 21.0, height = 29.7 },
                    margin = new { top = 0, bottom = 0, left = 0, right = 0 },
                }, cancellationToken).ConfigureAwait(false);
                return Convert.FromBase64String(printed.GetString()!);
            }
            finally
            {
                using var close = new HttpRequestMessage(HttpMethod.Delete, $"session/{session}");
                using var _ = await http.SendAsync(close, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            if (!driver.HasExited)
            {
                driver.Kill(entireProcessTree: true);
            }

            _one.Release();
        }
    }

    public void Dispose() => _one.Dispose();

    private static async Task<string> StartAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var body = new
        {
            capabilities = new { alwaysMatch = new Dictionary<string, object> { ["browserName"] = "firefox", ["moz:firefoxOptions"] = new { args = new[] { "-headless" } } } },
        };
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var value = await CallAsync(http, "session", body, cancellationToken).ConfigureAwait(false);
                return value.GetProperty("sessionId").GetString()!;
            }
            catch (HttpRequestException) when (attempt < 20)
            {
                // geckodriver needs a moment before it listens.
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<JsonElement> CallAsync(HttpClient http, string path, object body, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(path, body, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        var value = json.GetProperty("value");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Firefox could not print: {(value.TryGetProperty("message", out var m) ? m.GetString() : response.StatusCode.ToString())}");
        }

        return value.Clone();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
