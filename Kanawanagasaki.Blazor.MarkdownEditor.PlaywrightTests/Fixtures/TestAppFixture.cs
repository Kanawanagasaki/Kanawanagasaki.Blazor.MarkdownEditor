using System.Diagnostics;
using Microsoft.Playwright;
using Xunit;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Tests.Fixtures;

/// <summary>
/// xunit fixture that starts the Blazor WASM test app as a subprocess
/// and provides a shared Playwright browser instance for all tests.
/// </summary>
public class TestAppFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private Process? _appProcess;
    public IBrowser? Browser { get; private set; }
    public string BaseAddress { get; private set; } = null!;
    private readonly int _port = FindFreePort();

    private static int FindFreePort()
    {
        var listener = System.Net.Sockets.TcpListener.Create(0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async Task InitializeAsync()
    {
        // Start the Blazor WASM test app as a subprocess
        var appProjectDir = Path.Combine(
            Directory.GetCurrentDirectory(), "..", "..", "..", "..", "TestApp");
        appProjectDir = Path.GetFullPath(appProjectDir);

        if (!Directory.Exists(appProjectDir))
            throw new DirectoryNotFoundException($"TestApp directory not found: {appProjectDir}");

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "";
        var dotnetExe = Path.Combine(dotnetRoot, "dotnet");
        if (!File.Exists(dotnetExe))
            dotnetExe = "dotnet";

        _appProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = dotnetExe,
                Arguments = $"run --urls http://127.0.0.1:{_port}",
                WorkingDirectory = appProjectDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Environment =
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                    ["DOTNET_ROOT"] = dotnetRoot,
                }
            }
        };

        _appProcess.Start();
        BaseAddress = $"http://127.0.0.1:{_port}";

        // Wait for the WASM dev server to be ready
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var ready = false;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(60))
        {
            try
            {
                var response = await httpClient.GetAsync(BaseAddress);
                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    ready = true;
                    break;
                }
            }
            catch
            {
                // Server not ready yet
            }
            await Task.Delay(500);
        }

        if (!ready)
        {
            var stderr = _appProcess.StandardError.ReadToEnd();
            var stdout = _appProcess.StandardOutput.ReadToEnd();
            throw new InvalidOperationException(
                $"Test app failed to start within 60s at {BaseAddress}.\nStdout: {stdout}\nStderr: {stderr}");
        }

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"]
        });
    }

    public async Task<IPage> CreatePageAsync()
    {
        if (Browser == null)
            throw new InvalidOperationException("Browser not initialized. Ensure InitializeAsync was called.");

        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });

        var page = await context.NewPageAsync();
        return page;
    }

    public async Task DisposeAsync()
    {
        if (Browser != null)
        {
            await Browser.DisposeAsync();
        }
        _playwright?.Dispose();

        if (_appProcess != null && !_appProcess.HasExited)
        {
            _appProcess.Kill(entireProcessTree: true);
            _appProcess.WaitForExit(5000);
        }
    }
}
