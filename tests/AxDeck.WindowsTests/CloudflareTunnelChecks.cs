using System.Linq;
using AxDeck.Core;
using AxDeck.Native;

// Unit tests for the native Cloudflare tunnel logic. The side effects (RunCommand, the edge HTTP probe) are replaced by overrides
// so the decisions are checked without a live network or the real cloudflared/ssh on the build machine. The site table is no
// longer a constant in the code — it comes from services.json on the user's machine — so these tests inject a fixture config
// with placeholder hosts.
internal static class CloudflareTunnelChecks
{
    // The fixture the checks below assert on. Every host is an example.invalid name or a made-up alias.
    private static ServicesConfig Fixture() => new()
    {
        Tunnel = new()
        {
            Hostname = "example.invalid",
            DefaultSshHost = "gateway",
            SshHosts = ["gateway", "nas"],
            Ports = [10000, 12222],
            Sites =
            [
                new TunnelSite { Host = "example.invalid", Kind = "http", Label = "Site", Backend = "container:80", Path = "/" },
                new TunnelSite { Host = "panel.example.invalid", Kind = "http", Label = "Panel", Backend = "container:8080", Path = "/entrance" },
                new TunnelSite { Host = "gateway", Kind = "ssh", Label = "SSH", Backend = "ssh sshd:10000" },
                new TunnelSite { Host = "db.example.invalid", Kind = "tcp", Label = "Database", Backend = "postgres:5432", Proto = "tcp", Port = 15432, Client = "127.0.0.1:15432" },
                new TunnelSite { Host = "rdp.example.invalid", Kind = "rdp", Label = "Desktop", Backend = "rdp:3389", Proto = "tcp", Port = 13389, Client = "127.0.0.1:13389" },
            ],
        },
        Bge = new() { ModelPath = @"C:\models\model.gguf" },
    };

    public static void Run(Action<string, bool> check)
    {
        // The table the whole file reads comes from the config object, not from code.
        CloudflareTunnel.Config = Fixture();

        check("Sites follows the configured entries", CloudflareTunnel.Sites.Count == 5);
        check("Default SSH host comes from the config", CloudflareTunnel.DefaultSshHost == "gateway");
        check("Local ports come from the config", CloudflareTunnel.LocalPorts.Count == 2 && CloudflareTunnel.LocalPorts.Contains(10000) && CloudflareTunnel.LocalPorts.Contains(12222));
        check("SiteTable mirrors the configured sites", CloudflareTunnel.SiteTable().Count == 5);

        // client_command: HTTP opens in the browser, SSH uses the config's ProxyCommand, TCP starts a local forwarder.
        var http = CloudflareTunnel.SiteByHost("example.invalid")!.Value;
        check("HTTP client command is the site URL", CloudflareTunnel.ClientCommand(http) == "https://example.invalid/");
        var panel = CloudflareTunnel.SiteByHost("panel.example.invalid")!.Value;
        check("The panel keeps its /entrance path", CloudflareTunnel.ClientCommand(panel) == "https://panel.example.invalid/entrance");
        var ssh = CloudflareTunnel.SiteByHost("gateway")!.Value;
        check("SSH client command is ssh <host>", CloudflareTunnel.ClientCommand(ssh) == "ssh gateway");
        var pg = CloudflareTunnel.SiteByHost("db.example.invalid")!.Value;
        check("TCP client command starts cloudflared access", CloudflareTunnel.ClientCommand(pg).StartsWith("cloudflared access tcp"));

        // Unknown hosts and an empty config must not throw: the page is usable before services.json exists.
        check("An unknown host has no site", CloudflareTunnel.SiteByHost("nope.example.invalid") is null);
        CloudflareTunnel.Config = new ServicesConfig();
        check("An empty config has no sites", CloudflareTunnel.Sites.Count == 0 && CloudflareTunnel.DefaultSshHost == "");
        check("An empty config still builds a status snapshot", CloudflareTunnel.SiteTable().Count == 0);
        CloudflareTunnel.Config = Fixture();

        // explain_failure: the misleading Windows OpenSSH errors become actionable conclusions.
        check("A non-default host points at the local forwarder port", CloudflareTunnel.ExplainFailure("nas", "kex_exchange_identification: read: Connection reset by peer").Contains("127.0.0.1:12222"));
        check("The default host is not blamed on a forwarder", !CloudflareTunnel.ExplainFailure("gateway", "kex_exchange_identification: read: Connection reset by peer").Contains("转发器"));
        check("1033 is reported as the connector being offline", CloudflareTunnel.ExplainFailure("x", "websocket: bad handshake error 1033").Contains("1033"));
        check("permission denied mentions the key", CloudflareTunnel.ExplainFailure("x", "Permission denied (publickey).").Contains("公钥认证"));
        check("timeout mentions the tunnel connector", CloudflareTunnel.ExplainFailure("x", "Connection timed out").Contains("超时"));

        // DecideEdge: the 1033 detection and the "403/404 are not offline" rule, plus the transport-error retry.
        var offline1033 = CloudflareTunnel.DecideEdge(new CloudflareTunnel.EdgeHttpResponse("error 1033: connector offline", null, false, null, null));
        check("An edge body with 1033 is offline", offline1033.Online == false && offline1033.Detail!.Contains("1033"));
        var ok200 = CloudflareTunnel.DecideEdge(new CloudflareTunnel.EdgeHttpResponse("welcome", 200, false, null, null));
        check("HTTP 200 is online", ok200.Online == true);
        var not1033 = CloudflareTunnel.DecideEdge(new CloudflareTunnel.EdgeHttpResponse(null, 403, true, null, null));
        check("HTTP 403 (non-1033) is online", not1033.Online == true);
        var http530 = CloudflareTunnel.DecideEdge(new CloudflareTunnel.EdgeHttpResponse(null, 530, true, null, null));
        check("HTTP 530 is offline (1033 class)", http530.Online == false);
        var transport = CloudflareTunnel.DecideEdge(new CloudflareTunnel.EdgeHttpResponse(null, null, false, "WebException", "handshake timed out"));
        check("A transport error asks to retry (null verdict)", transport.Online is null);

        // EdgeStatus honours the HTTP override (no real network).
        CloudflareTunnel.HttpGetOverride = _ => new CloudflareTunnel.EdgeHttpResponse("error 1033", null, false, null, null);
        var edge = CloudflareTunnel.EdgeStatus();
        check("EdgeStatus surfaces 1033 through the override", edge.Online == false && edge.Detail.Contains("1033"));
        CloudflareTunnel.HttpGetOverride = null;

        // RunCommand honours the runner override (no real process).
        CloudflareTunnel.RunnerOverride = (_, _) => new CloudflareTunnel.RunResult(0, "v2026.5.0", "");
        var ran = CloudflareTunnel.RunCommand(["cloudflared", "--version"], 8);
        check("RunCommand uses the runner override", ran.ReturnCode == 0 && ran.Stdout == "v2026.5.0");
        CloudflareTunnel.RunnerOverride = null;

        // ProbeSites returns one entry per configured site with valid fields (real PortOpen/ProxyCommandOk are tolerated).
        CloudflareTunnel.HttpGetOverride = _ => new CloudflareTunnel.EdgeHttpResponse("ok", 200, false, null, null);
        var probes = CloudflareTunnel.ProbeSites();
        check("ProbeSites returns one probe per site", probes.Count == 5);
        check("Every probe has a host and label", probes.All(p => !string.IsNullOrEmpty(p.Host) && !string.IsNullOrEmpty(p.Label)));
        CloudflareTunnel.HttpGetOverride = null;

        // CollectStatus is cheap and side-effect free: it must not throw, and its shape is fixed.
        try
        {
            var snap = CloudflareTunnel.CollectStatus();
            check("CollectStatus returns the configured sites", snap.Sites.Count == 5);
            check("CollectStatus reports both local ports", snap.Ports.Count == 2);
        }
        catch (System.Exception ex)
        {
            check("CollectStatus does not throw (" + ex.GetType().Name + ")", false);
        }

        // Leave the static in its startup state so a later check file does not inherit the fixture.
        CloudflareTunnel.Config = new ServicesConfig();
    }
}