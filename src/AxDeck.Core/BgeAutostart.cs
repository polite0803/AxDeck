namespace AxDeck.Core;

// The BGE-M3 server can start with Windows on its own, without AxDeck: the llama-server command line is written into the
// Run key, so the embedding service is there as soon as the machine is logged in. The command line and the reasons it
// cannot be written live here, so they can be tested without touching the machine's registry.
public static class BgeAutostart
{
    // The name of the value in the Run key. It is not the one AxDeck itself uses, so the two can be turned off separately.
    public const string ValueName = "AxDeckBgeM3";

    // The command line the Run key holds: the server, then the model, the address and the port, then the embeddings mode.
    // Both paths are quoted, because a model path usually lives under "C:\Users\<name>\..." and may hold a space.
    public static string Command(string serverPath, string modelPath) =>
        $"\"{serverPath}\" -m \"{modelPath}\" --host 0.0.0.0 --port 8091 --embeddings";

    // Why the command line cannot be written, or null when it can. A missing server or model is the usual reason: both are
    // paths on one machine, and neither ships with AxDeck.
    public static string? Refusal(string? serverPath, string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(serverPath)) return Loc.T("未找到 llama-server.exe，无法设置开机自启。", "llama-server.exe was not found, so it cannot be set to start with Windows.");
        if (string.IsNullOrWhiteSpace(modelPath)) return Loc.T("模型未配置，无法设置开机自启。", "The model is not configured, so it cannot be set to start with Windows.");
        if (serverPath.Length + modelPath.Length > 400) return Loc.T("路径过长，无法设置开机自启。", "The paths are too long to be registered for startup.");
        return null;
    }
}
