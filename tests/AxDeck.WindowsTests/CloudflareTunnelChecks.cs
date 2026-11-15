using System.Linq;
using AxDeck.Native;

// Unit tests for the native Cloudflare tunnel logic. The side effects (RunCommand, the edge HTTP probe) are replaced by overrides
// so the decisions are checked without a live network or the real cloudflared/ssh on the build machine.
internal static class CloudflareTunnelChecks
{
    public static void Run(Action<string, bool> check)
    {
        // Constants and the site table.
        check("SITES has the 9 bgnone.cn entries", CloudflareTunnel.Sites.Count == 9);
        check("Default SSH host is ssh.bgnone.cn", CloudflareTunnel.DefaultSshHost == "ssh.bgnone.cn");
        check("Local ports are 10000 and 12222", CloudflareTunnel.LocalPorts.Count == 2 && CloudflareTunnel.LocalPorts.Contains(10000) && CloudflareTunnel.LocalPorts.Contains(12222));
        check("SiteTable mirrors the 9 sites", CloudflareTunnel.SiteTable().Count == 9);

        // client_command: HTTP opens in the browser, SSH uses the config's ProxyCommand, TCP starts a local forwarder.
        var http = CloudflareTunnel.SiteByHost("bgnone.cn")!.Value;
        check("HTTP client command is the site URL", CloudflareTunnel.ClientCommand(http) == "https://bgnone.cn/");
        var panel = CloudflareTunnel.SiteByHost("1panel.bgnone.cn")!.Value;
        check("1Panel keeps its /entrance path", CloudflareTunnel.ClientCommand(panel) == "https://1panel.bgnone.cn/entrance");
        var ssh = CloudflareTunnel.SiteByHost("ssh.bgnone.cn")!.Value;
        check("SSH client command is ssh <host>", CloudflareTunnel.ClientCommand(ssh) == "ssh ssh.bgnone.cn");
        var pg = CloudflareTunnel.SiteByHost("postgres.bgnone.cn")!.Value;
        check("TCP client command starts cloudflared access", CloudflareTunnel.ClientCommand(pg).StartsWith("cloudflared access tcp"));

        // explain_failure: the misleading Windows OpenSSH errors become actionable conclusions.
        check("nas-tunnel points at the missing 12222 forwarder", CloudflareTunnel.ExplainFailure("nas-tunnel", "kex_exchange_identification: read: Connection reset by peer").Contains("127.0.0.1:12222"));
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

        // ProbeSites returns one entry per site with valid fields (real PortOpen/ProxyCommandOk are tolerated).
        CloudflareTunnel.HttpGetOverride = _ => new CloudflareTunnel.EdgeHttpResponse("ok", 200, false, null, null);
        var probes = CloudflareTunnel.ProbeSites();
        check("ProbeSites returns one probe per site", probes.Count == 9);
        check("Every probe has a host and label", probes.All(p => !string.IsNullOrEmpty(p.Host) && !string.IsNullOrEmpty(p.Label)));
        CloudflareTunnel.HttpGetOverride = null;

        // CollectStatus is cheap and side-effect free: it must not throw, and its shape is fixed.
        try
        {
            var snap = CloudflareTunnel.CollectStatus();
            check("CollectStatus returns the 9 sites", snap.Sites.Count == 9);
            check("CollectStatus reports both local ports", snap.Ports.Count == 2);
        }
        catch (System.Exception ex)
        {
            check("CollectStatus does not throw (" + ex.GetType().Name + ")", false);
        }
    }
}
