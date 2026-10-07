using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FamicomPlayer;

internal static class Program
{
    internal static string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FamicomPlayerNative");
    internal static string[] Arguments = Array.Empty<string>();
    // Chrome supplies its own host arguments; the test runner scopes this environment
    // variable to an isolated browser process and its children.
    internal static bool BrowserSmoke => Arguments.Contains("--browser-smoke") || Environment.GetEnvironmentVariable("FAMICOMPLAYER_BROWSER_SMOKE") == "1";
    internal static bool InteractionSmoke => Arguments.Contains("--interaction-smoke");
    internal static bool ObsSmoke => Arguments.Contains("--obs-smoke");
    internal static bool Smoke => Arguments.Contains("--smoke") || Arguments.Contains("--startup-smoke") || BrowserSmoke || InteractionSmoke || ObsSmoke;

    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        if (args.Contains("--self-test")) return SelfTests.Run();
        if (args.Contains("--browser-bridge-info"))
        {
            using var output = Console.OpenStandardOutput();
            System.Text.Json.JsonSerializer.Serialize(output, new { protocol = 1, extensionId = BrowserIntegration.ExtensionId });
            return 0;
        }
        if (args.Length > 0 && args[0].StartsWith("chrome-extension://", StringComparison.Ordinal))
            return BrowserIntegration.RunHost(args[0]).GetAwaiter().GetResult();
        if (Smoke) DataDirectory = Path.Combine(Path.GetTempPath(), ObsSmoke ? "FamicomPlayerNative-ObsSmoke" : InteractionSmoke ? "FamicomPlayerNative-InteractionSmoke" : BrowserSmoke ? "FamicomPlayerNative-BrowserSmoke" : "FamicomPlayerNative-Smoke");
        Directory.CreateDirectory(DataDirectory);
        using var instance = new Mutex(true, BrowserSmoke ? "Local\\FamicomPlayerNative-BrowserSmoke" : Smoke ? "Local\\FamicomPlayerNative-Smoke" : "Local\\FamicomPlayerNative", out bool first);
        if (!first)
        {
            var link = args.FirstOrDefault(arg => arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
            if (link == null) return 0;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                var request = BrowserIntegration.Validate(new BrowserIntegration.Request("play", link));
                var reply = BrowserIntegration.TrySend(request, 5000, timeout.Token).GetAwaiter().GetResult();
                return reply?.Ok == true ? 0 : 1;
            }
            catch { return 1; }
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            File.AppendAllText(Path.Combine(DataDirectory, "error.log"), DateTime.Now + " " + e.Exception + Environment.NewLine);
        };
        try
        {
            var window = new WidgetWindow();
            using var listener = new BrowserIntegration.Listener(url =>
                app.Dispatcher.InvokeAsync(() => window.PlayFromBrowser(url)).Task.Unwrap());
            return app.Run(window);
        }
        catch (Exception error)
        {
            File.AppendAllText(Path.Combine(DataDirectory, "error.log"), error + Environment.NewLine);
            if (!Smoke) ThemedDialog.Notify(null, "FamicomPlayer", "위젯을 시작하지 못했습니다.\n" + error.Message);
            return 1;
        }
    }
}
