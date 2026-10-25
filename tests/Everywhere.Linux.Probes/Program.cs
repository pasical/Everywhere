using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Everywhere.Automation;
using Everywhere.Extensions;
using Everywhere.Linux.Automation;
using Everywhere.Linux.Platform;
using Everywhere.Linux.ProcessIsolation;
using Everywhere.ProcessIsolation.Automation;
using Everywhere.ProcessIsolation.Hosting;
using Everywhere.ProcessIsolation.Roles;
using Everywhere.ProcessIsolation.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Everywhere.Linux.Probes;

/// <summary>Explicit native probes. Run only in the disposable compositor created by isolated.py.</summary>
public static partial class Program
{
    public static string[] Arguments { get; private set; } = [];

    [STAThread]
    public static int Main(string[] args)
    {
        Serilog.Log.Logger = new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Console().CreateLogger();
        if (ProcessRoleCommandLine.ParseHostsControl(args) is not null ||
            ProcessRoleCommandLine.Parse(args) != ProcessRole.Main || args.FirstOrDefault() == "--hyprland-shortcut")
        {
            Everywhere.Linux.Program.Main(args);
            return Environment.ExitCode;
        }
        Arguments = args;
        if (args.FirstOrDefault() == "--observe")
        {
            try { return Observe(args); }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.FirstOrDefault() == "--accessibility-unavailable")
        {
            using var client = new AtspiClient();
            try { client.Call(() => AtspiNative.atspi_get_desktop(0)); }
            catch (NotSupportedException)
            {
                Console.WriteLine("ACCESSIBILITY_UNAVAILABLE_HANDLED");
                return 0;
            }
            throw new InvalidOperationException("The unavailable-bus probe unexpectedly connected.");
        }
        if (args.FirstOrDefault() == "--native") return RunNativeAsync(args).GetAwaiter().GetResult();
        return AppBuilder.Configure<ProbeApplication>().UsePlatformDetect().UseWaylandWithFallback()
            .AfterPlatformServicesSetup(_ => HyprlandWindowingPlatform.Install())
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    private static async Task<int> RunNativeAsync(string[] args)
    {
        using var backend = new HyprlandVisualElementBackend();
        var window = backend.ReadDesktop().Windows.Single(item => item.Title == "Everywhere controlled fixture");
        using var context = new VisualContext();
        using var turn = context.BeginTurn();
        using var retention = context.CreateRetention();
        var root = backend.GetWindow(retention, window);
        using var snapshot = VisualContextSnapshotter.CreateSnapshot(context, [root],
            allowedTraverseDirections: VisualContextTraverseDirections.Child);
        var prompt = VisualContextPromptBuilder.Build(context, snapshot);
        File.WriteAllText(args[1], prompt);
        var button = Find(root, VisualElementType.Button, "Everywhere target button", retention);
        var entry = Find(root, VisualElementType.TextEdit, null, retention);
        if (button is null || entry is null) throw new InvalidOperationException("The real GTK provider did not expose the required fixture controls.\n" + prompt);
        entry.SetText("Hyprland Unicode 🌏 漢字 e\u0301");
        var text = entry.ReadText(0, 4096);
        if (text.Text != "Hyprland Unicode 🌏 漢字 e\u0301") throw new InvalidOperationException("Native Unicode text did not round-trip.");
        var bound = button.Query(new VisualElementQueryRequest(VisualElementFields.All, 100));
        if (bound.Snapshot.Bounds is not { Width: > 0, Height: > 0 }) throw new InvalidOperationException("The button has no usable window-relative bounds.");
        var counterPath = Path.Combine(Path.GetDirectoryName(args[1]) ?? ".", "fixture.json");
        var clicks = ReadClicks(counterPath);
        button.Invoke();
        await WaitForClicksAsync(counterPath, ++clicks);
        var provider = false;
        if (args.Contains("--provider"))
        {
            Console.Write("DeepSeek test key (hidden): ");
            var secret = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace) { if (secret.Length != 0) secret.Length--; }
                else if (!char.IsControl(key.KeyChar)) secret.Append(key.KeyChar);
            }
            Console.WriteLine();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret.ToString());
            secret.Clear();
            using var response = await client.PostAsJsonAsync("https://api.deepseek.com/chat/completions", new
            {
                model = "deepseek-flash", max_tokens = 256, thinking = new { type = "disabled" },
                messages = new[] { new { role = "user", content = "Choose the integer target ID of the button named Everywhere target button. Use choose_target.\n" + prompt } },
                tools = new[] { new { type = "function", function = new { name = "choose_target", description = "Select the requested UI button", parameters = new { type = "object", properties = new { target = new { type = "integer" } }, required = new[] { "target" } } } } },
                tool_choice = "required",
            });
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var arguments = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments").GetString();
            using var selected = JsonDocument.Parse(arguments ?? "{}");
            var id = selected.RootElement.GetProperty("target").GetInt32();
            if (!context.TryGetTarget(id, out var target) || target is not ElementTarget element || !ReferenceEquals(element.Element, button))
                throw new InvalidOperationException("The provider selected a different target.");
            element.Element.Invoke();
            await WaitForClicksAsync(counterPath, ++clicks);
            provider = true;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { NativeGtkTree = true, UnicodeRoundTrip = true, SemanticInvoke = true, DeepSeekFlash = provider, Window = window.Bounds, Button = bound.Snapshot.Bounds, context.TargetCount }));
        return 0;
    }

    private static int ReadClicks(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("clicks").GetInt32();
    }

    private static async Task WaitForClicksAsync(string path, int expected)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (ReadClicks(path) == expected) return;
            await Task.Delay(25);
        }
        throw new InvalidOperationException("The semantic action did not change the controlled GTK fixture.");
    }

    private static VisualElement? Find(VisualElement root, VisualElementType type, string? name, VisualElementRetention retention)
    {
        var queue = new Queue<VisualElement>(); queue.Enqueue(root);
        for (var count = 0; queue.TryDequeue(out var next) && count < 256; count++)
        {
            var observation = next.Query(new VisualElementQueryRequest(VisualElementFields.Type | VisualElementFields.Name, 0));
            if (observation.Snapshot.Type == type && (name is null || observation.Snapshot.Name == name)) return next;
            using var children = next.CreateEnumerator(VisualElementRelation.Child, new VisualElementQueryRequest(VisualElementFields.Type, 0));
            while (children.MoveNext())
            {
                if (children.Current.Result is not { } child) continue;
                retention.Retain(child.Element);
                queue.Enqueue(child.Element);
            }
        }
        return null;
    }
}

public sealed class ProbeApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new ShadUI.ShadTheme());
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Everywhere.Core/")) { Source = new Uri("avares://Everywhere.Core/Views/ScreenSelection/ScreenSelectionToolTip.axaml") });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        Dispatcher.UIThread.Post(() => RunAsync().Detach());
    }

    private async Task RunAsync()
    {
        var lifetime = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime ?? throw new InvalidOperationException();
        try
        {
            await using var services = new ServiceCollection()
                .AddSingleton<INamedPipePeerVerifier, LinuxNamedPipePeerVerifier>()
                .AddProcessIsolation()
                .BuildServiceProvider();
            var hosts = services.GetRequiredService<HostProcessCoordinator>();
            await hosts.InitializeAsync(CancellationToken.None);
            var visuals = services.GetRequiredService<ChatVisualService>();
            using var helper = new LinuxWindowHelper();
            var service = new HyprlandScreenSelectionService(helper, new LinuxNativeHelper());
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            if (Program.Arguments.Contains("--passive-overlay"))
            {
                var window = new Everywhere.Views.VisualElementOverlayWindow
                {
                    Title = "Everywhere passive effect probe",
                    Content = new Border { BorderBrush = Avalonia.Media.Brushes.DodgerBlue, BorderThickness = new Thickness(3) },
                };
                var bounds = window.Screens.All[0].Bounds;
                window.Position = bounds.Position;
                window.Width = bounds.Width / window.DesktopScaling;
                window.Height = bounds.Height / window.DesktopScaling;
                helper.InitializeWindow(window);
                helper.SetWindowProperties(window, Everywhere.Interop.WindowInputMode.NonActivating, false, Everywhere.Interop.WindowLayer.Overlay);
                try
                {
                    window.Show();
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellation.Token);
                    File.WriteAllText(Program.Arguments[1], JsonSerializer.Serialize(new
                    {
                        Ready = true, window.DesktopScaling, window.RenderScaling, window.Position, window.Width, window.Height,
                        Screens = window.Screens.All.Select(screen => new { screen.DisplayName, screen.Bounds, screen.Scaling }),
                    }));
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellation.Token);
                }
                finally { window.Hide(); }
                lifetime.Shutdown(0);
                return;
            }
            if (Program.Arguments.Contains("--screenshot"))
            {
                using var bitmap = await service.TakeScreenshotAsync(visuals.AcquisitionContext,
                    Everywhere.Interop.ScreenSelectionMode.Free, cancellation.Token);
                if (Program.Arguments.Contains("--cancel"))
                {
                    if (bitmap is not null) throw new InvalidOperationException("The cancelled screenshot returned pixels.");
                    File.WriteAllText(Program.Arguments[1], "{\"Cancelled\":true}");
                }
                else
                {
                    if (bitmap is null) throw new InvalidOperationException("The screenshot did not confirm a region.");
                    bitmap.Save(Path.ChangeExtension(Program.Arguments[1], ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    File.WriteAllText(Program.Arguments[1], JsonSerializer.Serialize(new { bitmap.PixelSize.Width, bitmap.PixelSize.Height }));
                }
                lifetime.Shutdown(0);
                return;
            }
            using var anchor = Program.Arguments.Contains("--element") ?
                await service.PickVisualElementAsync(visuals.AcquisitionContext, Everywhere.Interop.ScreenSelectionMode.Element, cancellation.Token) :
                await service.PickApplicationWindowAsync(visuals.AcquisitionContext, cancellation.Token);
            if (Program.Arguments.Contains("--cancel"))
            {
                if (anchor is not null) throw new InvalidOperationException("The cancelled picker returned an anchor.");
                File.WriteAllText(Program.Arguments[1], "{\"Cancelled\":true}");
                lifetime.Shutdown(0);
                return;
            }
            if (anchor is null) throw new InvalidOperationException("The production picker did not confirm an anchor.");
            var observation = anchor.Snapshot;
            var result = new { observation.Id, observation.Name, observation.ProcessId, observation.Bounds,
                NativeWindow = observation.NativeWindowHandle?.ToString("X") };
            File.WriteAllText(Program.Arguments[1], JsonSerializer.Serialize(result));
            Console.WriteLine("PICKER_CONFIRMED " + JsonSerializer.Serialize(result));
            lifetime.Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            lifetime.Shutdown(1);
        }
    }
}
