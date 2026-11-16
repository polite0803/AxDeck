using AxDeck.Core;

// services.json holds what must not ship with AxDeck: the SSH hosts, the tunnel entries and the model path of one machine.
// The checks here are about the file being absent, hand-written and partly wrong, because that is what the page meets.
internal static class ServicesConfigTests
{
    public static void Run(string root, Action<string, bool> check)
    {
        var directory = Path.Combine(root, "services-config");
        Directory.CreateDirectory(directory);

        // No file: the page still has something to work with, and knows where the file would be.
        var missing = ServicesConfig.Load(directory);
        check("a missing services.json loads as an empty config", missing.Tunnel.Sites.Count == 0 && missing.Bge.ModelPath == "" && !missing.HasTunnel);
        check("a missing services.json still knows its path", missing.FilePath == Path.Combine(directory, ServicesConfig.FileName));

        // The template AxDeck writes: placeholder hosts only, camelCase, and readable by the loader.
        File.WriteAllText(Path.Combine(directory, ServicesConfig.FileName), new ServicesConfig().Template());
        var text = File.ReadAllText(Path.Combine(directory, ServicesConfig.FileName));
        check("the template carries no machine of its own", !text.Contains("bgnone", StringComparison.OrdinalIgnoreCase)
            && !text.Contains(@"C:\Users", StringComparison.OrdinalIgnoreCase) && text.Contains("example.com"));
        check("the template is written in camelCase", text.Contains("\"sshHosts\"") && text.Contains("\"modelPath\"") && !text.Contains("\"SshHosts\""));
        var templated = ServicesConfig.Load(directory);
        check("the template reads back into a usable config", templated.HasTunnel && templated.Tunnel.Sites.Count == 2 && templated.Bge.ModelPath.Length > 0);

        // A hand-written file: an unknown kind, an empty host and an out-of-range port are dropped instead of throwing.
        File.WriteAllText(Path.Combine(directory, ServicesConfig.FileName), """
            {
              "tunnel": {
                "hostname": "host.example.com",
                "sshHosts": ["host.example.com", ""],
                "ports": [10000, 0, 70000],
                "sites": [
                  { "host": "host.example.com", "kind": "http", "path": "/" },
                  { "host": "db.example.com", "kind": "tcp", "proto": "tcp", "port": 15432, "client": "127.0.0.1:15432" },
                  { "host": "desk.example.com", "kind": "rdp", "proto": "tcp", "port": 13389 },
                  { "host": "x.example.com", "kind": "carrier-pigeon" },
                  { "host": "", "kind": "http" }
                ]
              },
              "bge": { "modelPath": "D:\\models\\bge-m3.gguf" }
            }
            """);
        var edited = ServicesConfig.Load(directory);
        check("usable sites are kept and the rest dropped", edited.Tunnel.Sites.Count == 3);
        check("blank ssh hosts are dropped", edited.Tunnel.SshHosts.Count == 1);
        check("ports outside 1-65535 are dropped", edited.Tunnel.Ports.Count == 1 && edited.Tunnel.Ports[0] == 10000);
        check("the model path is read as written", edited.Bge.ModelPath == @"D:\models\bge-m3.gguf");
        check("a default ssh host falls back to the first listed host", edited.Tunnel.DefaultSshHost == ""
            && edited.Tunnel.SshHosts.FirstOrDefault() == "host.example.com");

        // A broken file: JSON that does not parse must leave the page empty, not crash it.
        File.WriteAllText(Path.Combine(directory, ServicesConfig.FileName), "{ not json");
        var broken = ServicesConfig.Load(directory);
        check("a broken services.json loads as an empty config", broken.Tunnel.Sites.Count == 0 && broken.Bge.ModelPath == "");
        check("a broken services.json still knows its path", broken.FilePath == Path.Combine(directory, ServicesConfig.FileName));

        // Property names are read case-insensitively, so a file typed by hand in PascalCase still works.
        File.WriteAllText(Path.Combine(directory, ServicesConfig.FileName), """{"Tunnel":{"Hostname":"h.example.com","SshHosts":["h.example.com"]}}""");
        check("a PascalCase file is read too", ServicesConfig.Load(directory).Tunnel.Hostname == "h.example.com");

        Directory.Delete(directory, recursive: true);
    }
}