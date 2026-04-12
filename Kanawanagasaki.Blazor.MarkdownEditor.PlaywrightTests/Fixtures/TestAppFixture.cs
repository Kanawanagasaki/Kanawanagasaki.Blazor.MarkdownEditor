using System.Diagnostics;
using Microsoft.Playwright;
using Xunit;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Tests.Fixtures;

/// <summary>
/// xunit collection fixture that:
///   1. Starts the TestApp via `dotnet run` as a background process
///      (properly handles Blazor WASM static web assets)
///   2. Waits for the server to be ready
///   3. Provides a shared Playwright Firefox browser instance for all tests.
/// </summary>
public class TestAppFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private Process? _serverProcess;
    public IBrowser? Browser { get; private set; }
    public string ServerUrl { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Find the TestApp project directory
        var testAppDir = FindTestAppDir();

        // Start the TestApp via dotnet run on a random port
        var port = Random.Shared.Next(5000, 6000);
        ServerUrl = $"http://127.0.0.1:{port}";

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --urls \"http://127.0.0.1:{port}\" --no-launch-profile",
            WorkingDirectory = testAppDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        _serverProcess = Process.Start(startInfo);

        // Wait for the server to be ready by polling
        using var client = new HttpClient();
        var ready = false;
        for (int i = 0; i < 120; i++) // up to 120 seconds
        {
            try
            {
                var response = await client.GetAsync($"{ServerUrl}/", CancellationToken.None);
                if (response.IsSuccessStatusCode)
                {
                    ready = true;
                    break;
                }
            }
            catch
            {
                // Server not ready yet
            }
            await Task.Delay(1000);
        }

        if (!ready)
        {
            throw new TimeoutException(
                $"TestApp did not start within 60 seconds. URL: {ServerUrl}");
        }

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
    }

    public async Task<IPage> CreatePageAsync()
    {
        if (Browser == null)
            throw new InvalidOperationException("Browser not initialized.");

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

        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            _serverProcess.Kill(entireProcessTree: true);
            _serverProcess.WaitForExit(5000);
        }
        _serverProcess?.Dispose();
    }

    /// <summary>
    /// Finds the TestApp project directory by walking up from the test
    /// assembly location to the solution root.
    /// </summary>
    private static string FindTestAppDir()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory!;
        var dir = new DirectoryInfo(baseDir);

        while (dir != null && !dir.GetFiles("*.slnx").Any())
        {
            dir = dir.Parent;
        }

        if (dir == null)
            throw new DirectoryNotFoundException("Could not find solution root.");

        var resolved = Path.Combine(dir.FullName,
            "Kanawanagasaki.Blazor.MarkdownEditor.TestApp");

        if (!Directory.Exists(resolved))
            throw new DirectoryNotFoundException(
                $"TestApp not found at {resolved}.");

        return resolved;
    }
}
