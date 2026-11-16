using System.Text.Json;
using System.Text.Json.Serialization;

namespace AxDeck.Core;

// The services & tools page is driven by config on this machine only: which SSH hosts and tunnel entries exist, and where the
// BGE-M3 model file is. None of it ships with AxDeck — the repository and the published binaries carry no host names, ports or
// paths — because it describes one person's own network. The file is services.json beside settings.json; AxDeck reads it every
// time the page refreshes, so an edit takes effect without a restart.
public sealed class ServicesConfig
{
    public const string FileName = "services.json";

    public TunnelConfig Tunnel { get; set; } = new();
    public BgeConfig Bge { get; set; } = new();

    // Where the file was read from. Set by Load, never written into the file itself.
    [JsonIgnore]
    public string FilePath { get; set; } = "";

    // camelCase both ways: the file is written and read by hand, and the template AxDeck writes is what the docs show.
    // Reading stays case-insensitive, so an entry typed as "SshHosts" is taken too.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Reads the file in the data folder. A missing file is not an error: the page then shows how to write one.
    public static ServicesConfig Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return new() { FilePath = path };
        try
        {
            var config = JsonSerializer.Deserialize<ServicesConfig>(File.ReadAllText(path), Options) ?? new();
            config.FilePath = path;
            return config.Normalized();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new() { FilePath = path };
        }
    }

    // Drops the entries that cannot be used, so a hand-written file with a typo leaves the page usable instead of throwing.
    public ServicesConfig Normalized()
    {
        var tunnel = Tunnel ?? new();
        tunnel.Sites = [.. (tunnel.Sites ?? []).Where(site => !string.IsNullOrWhiteSpace(site.Host) && TunnelSiteKind.IsKnown(site.Kind))];
        tunnel.SshHosts = [.. (tunnel.SshHosts ?? []).Where(host => !string.IsNullOrWhiteSpace(host))];
        tunnel.Ports = [.. (tunnel.Ports ?? []).Where(port => port is > 0 and < 65536)];
        Tunnel = tunnel;
        Bge ??= new();
        return this;
    }

    // True when the file named the tunnel at all; the page says so instead of showing an empty list.
    [JsonIgnore]
    public bool HasTunnel => Tunnel.Sites.Count > 0 || !string.IsNullOrWhiteSpace(Tunnel.Hostname);

    // The template written when the user asks to open a file that does not exist yet. Every value is a placeholder.
    public string Template() => JsonSerializer.Serialize(new ServicesConfig
    {
        Tunnel = new()
        {
            Hostname = "your-host.example.com",
            DefaultSshHost = "your-host.example.com",
            SshHosts = ["your-host.example.com", "nas"],
            Ports = [10000],
            Sites =
            [
                new TunnelSite { Host = "your-host.example.com", Kind = "http", Label = "Site", Backend = "container:8080", Path = "/" },
                new TunnelSite { Host = "db.your-host.example.com", Kind = "tcp", Label = "Database", Backend = "postgres:5432", Proto = "tcp", Port = 15432, Client = "127.0.0.1:15432" },
            ],
        },
        Bge = new() { ModelPath = @"C:\models\bge-m3.Q5_K_M.gguf" },
    }, Options);
}

public sealed class TunnelConfig
{
    // The host the "public 443" and "edge status" readings probe.
    public string Hostname { get; set; } = "";
    // The SSH host name used by the login test and the remote container actions (a Host in the SSH config).
    public string DefaultSshHost { get; set; } = "";
    public List<string> SshHosts { get; set; } = [];
    // Local ports the TCP/RDP forwarders listen on, shown in the environment block.
    public List<int> Ports { get; set; } = [];
    public List<TunnelSite> Sites { get; set; } = [];
}

public sealed class BgeConfig
{
    // The bge-m3 gguf file. Empty reads as "no model configured".
    public string ModelPath { get; set; } = "";
}

// One entry of the site list. kind is http, tcp, rdp or ssh; a tcp/rdp entry needs Proto and Port (the local port cloudflared
// access listens on), and Client says where a client should connect.
public sealed class TunnelSite
{
    public string Host { get; set; } = "";
    public string Kind { get; set; } = "http";
    public string Label { get; set; } = "";
    public string Backend { get; set; } = "";
    public string? Path { get; set; }
    public string? Proto { get; set; }
    public int? Port { get; set; }
    public string? Client { get; set; }
}

public static class TunnelSiteKind
{
    public const string Http = "http", Tcp = "tcp", Rdp = "rdp", Ssh = "ssh";

    public static bool IsKnown(string? kind) => kind is Http or Tcp or Rdp or Ssh;
}