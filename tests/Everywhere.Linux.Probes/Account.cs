using System.Reflection;
using System.Text.Json;
using System.Web;
using Everywhere.Cloud;
using Microsoft.Extensions.DependencyInjection;
using Everywhere.Interop;
using Everywhere.Linux.Platform;
using Everywhere.Linux.ProcessIsolation;
using Everywhere.ProcessIsolation.Activation;

namespace Everywhere.Linux.Probes;

partial class Program
{
    private static int CheckAccountConfiguration(string reportPath)
    {
        var assembly = typeof(OAuthCloudClient).Assembly;
        var constants = assembly.GetType("Everywhere.Cloud.CloudConstants") ?? throw new MissingMemberException();
        var clientId = constants.GetField("ClientId")?.GetRawConstantValue() as string;
        var flowType = typeof(OAuthCloudClient).GetNestedType("InteractiveOAuthFlow", BindingFlags.NonPublic) ?? throw new MissingMemberException();
        using var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var factory = (IHttpClientFactory)(services.GetService(typeof(IHttpClientFactory)) ?? throw new InvalidOperationException());
        using var flow = Activator.CreateInstance(flowType, factory, CancellationToken.None) as IDisposable ?? throw new InvalidOperationException();
        var build = flowType.GetMethod("BuildAuthorizeUrl") ?? throw new MissingMethodException();
        var uri = new Uri((string)(build.Invoke(flow, null) ?? throw new InvalidOperationException()));
        var query = HttpUtility.ParseQueryString(uri.Query);
        var isConfigured = uri.Scheme == "https" && !string.IsNullOrEmpty(clientId) && query["client_id"] == clientId;
        if (!isConfigured) throw new InvalidOperationException("The build's account endpoint/client configuration is missing.");
        var callback = "sylinko-everywhere://callback?code=synthetic-code&state=" + Uri.EscapeDataString(query["state"] ?? "");
        flowType.GetMethod("HandleCallback")?.Invoke(flow, [callback]);
        var wait = flowType.GetMethod("WaitForCode")?.Invoke(flow, null) as Task<string> ?? throw new InvalidOperationException();
        if (wait.GetAwaiter().GetResult() != "synthetic-code") throw new InvalidOperationException("The synthetic callback failed.");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            HasAccountConfiguration = true,
            AuthorizationHost = uri.Host,
            HasPkceChallenge = query["code_challenge"]?.Length == 43,
            RedirectScheme = new Uri(query["redirect_uri"] ?? "").Scheme,
            SyntheticStateCallbackAccepted = true,
            TokenExchangePerformed = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static async Task<int> CheckProtocolCallbackAsync(string reportPath)
    {
        await using var server = ApplicationActivationEntrance.TryCreateServer([], new LinuxNamedPipePeerVerifier())
            ?? throw new InvalidOperationException("Run the protocol probe without another instance of this probe.");
        var launcher = new LinuxDesktopLauncher(BetterBclLauncher.Shared);
        if (!await launcher.EnsureUriSchemeHandlerAsync(new Uri("sylinko-everywhere://callback")))
            throw new InvalidOperationException("The protocol handler could not be registered.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        const string callback = "sylinko-everywhere://callback?state=synthetic-state&code=synthetic-code";
        var start = new System.Diagnostics.ProcessStartInfo("xdg-open") { UseShellExecute = false, ArgumentList = { callback } };
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException();
        await foreach (var request in server.ReadPendingAsync(timeout.Token))
        {
            if (request is not UrlCallbackActivationRequest received || received.Url != callback) continue;
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { CallbackRoutedToExistingProcess = true, TokenExchangePerformed = false }));
            return 0;
        }
        return 1;
    }
}
