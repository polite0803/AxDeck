using System.Linq;
using AxDeck.Native;

// Unit tests for the native BGE-M3 embedding service, the port of the old "BGE-M3 控制面板". The HTTP reads are
// replaced by an override so the decisions (the health verdict, the slot statistics, the request counter) are
// checked without a running llama-server and without the model file.
internal static class BgeM3Checks
{
    public static void Run(Action<string, bool> check)
    {
        // The settings the old panel hard-coded.
        check("The model path is the local bge-m3 gguf", BgeM3Service.ModelPath == @"E:\llama-models\bge-m3.Q5_K_M.gguf");
        check("The server binds 0.0.0.0", BgeM3Service.Host == "0.0.0.0");
        check("The server listens on port 8091", BgeM3Service.Port == 8091);
        check("The base url is the local port", BgeM3Service.BaseUrl == "http://localhost:8091");
        check("The embeddings API is the v1 path", BgeM3Service.EmbeddingsApi == "http://localhost:8091/v1/embeddings");

        // The llama-server command line: the model, the bind address, the port and the embeddings mode.
        var args = BgeM3Service.BuildArguments(BgeM3Service.ModelPath, BgeM3Service.Host, BgeM3Service.Port);
        check("The command line loads the model", args[0] == "-m" && args[1] == BgeM3Service.ModelPath);
        check("The command line binds the host", args[2] == "--host" && args[3] == "0.0.0.0");
        check("The command line sets the port", args[4] == "--port" && args[5] == "8091");
        check("The command line turns on the embeddings mode", args[6] == "--embeddings" && args.Count == 7);

        // Where llama-server.exe is looked for.
        var candidates = BgeM3Service.ServerCandidates();
        check("The WinGet llama.cpp package is searched", candidates.Any(p => p.Contains("ggml.llamacpp") && p.EndsWith("llama-server.exe")));
        check("The WinGet links folder is searched", candidates.Any(p => p.Contains("WinGet") && p.Contains("Links")));
        check("Every candidate is a llama-server.exe", candidates.All(p => p.EndsWith(BgeM3Service.ServerFileName)));

        // /health.
        check("A health body with status ok is healthy", BgeM3Service.ParseHealth("{\"status\":\"ok\"}"));
        check("A health body without ok is not healthy", !BgeM3Service.ParseHealth("{\"status\":\"loading model\"}"));
        check("A broken health body is not healthy", !BgeM3Service.ParseHealth("not json"));
        check("An empty health body is not healthy", !BgeM3Service.ParseHealth(""));

        // While the model loads, /health answers with an error status and a "Loading model" message.
        var loading = "{\"error\":{\"message\":\"Loading model\",\"type\":\"unavailable_error\",\"code\":503}}";
        check("A loading model is not healthy yet", !BgeM3Service.ParseHealth(loading));
        check("A loading model is told apart from a server that is gone", BgeM3Service.IsLoading(loading));
        check("A healthy server is not loading", !BgeM3Service.IsLoading("{\"status\":\"ok\"}"));

        // /slots: busy slots, idle slots, the token counter and the task ids.
        var slots = BgeM3Service.ParseSlots("[{\"id_task\":1,\"is_processing\":true,\"n_prompt_tokens_processed\":120},{\"id_task\":2,\"is_processing\":false,\"n_prompt_tokens_processed\":30}]");
        check("The slot count is the number of slots", slots.Total == 2);
        check("The busy slots are counted", slots.Busy == 1);
        check("The idle slots are the rest", slots.Idle == 1);
        check("The prompt tokens are summed", slots.Tokens == 150);
        check("The task ids are kept in order", slots.Tasks.SequenceEqual(new[] { 1, 2 }));
        check("The highest task id is reported", slots.MaxTaskId == 2);
        var empty = BgeM3Service.ParseSlots("[]");
        check("An empty slot list has no slots", empty.Total == 0 && empty.Busy == 0 && empty.Idle == 0);
        var missing = BgeM3Service.ParseSlots("[{\"id\":0,\"is_processing\":false}]");
        check("A slot without fields counts as idle", missing.Busy == 0 && missing.Idle == 1 && missing.Tokens == 0);
        check("A slot without a task id reports no task", missing.MaxTaskId == -1);

        // llama.cpp writes the task id only on the slots that ran a task; the rest of the slots carry no id at all.
        var real = BgeM3Service.ParseSlots("[{\"id\":0,\"n_ctx\":8192,\"speculative\":false,\"is_processing\":false},{\"id\":1,\"n_ctx\":8192,\"speculative\":false,\"is_processing\":true,\"id_task\":3,\"n_prompt_tokens\":5,\"n_prompt_tokens_processed\":5}]");
        check("The real slot list counts the busy slot", real.Total == 2 && real.Busy == 1 && real.Idle == 1);
        check("The real slot list counts the prompt tokens", real.Tokens == 5);
        check("The real slot list reports the task id of the used slot", real.MaxTaskId == 3);

        // The token counter as the old panel showed it.
        check("Tokens below a thousand are shown as they are", BgeM3Service.FormatTokens(812) == "812");
        check("Tokens above a thousand are shown in k", BgeM3Service.FormatTokens(3456) == "3.5k");
        check("A thousand tokens are shown as 1.0k", BgeM3Service.FormatTokens(1000) == "1.0k");

        // Poll: the service state and the request counter, driven by the HTTP override.
        var service = new BgeM3Service();
        string slotsBody = "[{\"id\":0,\"is_processing\":false},{\"id\":1,\"is_processing\":true,\"id_task\":0,\"n_prompt_tokens_processed\":100}]";
        BgeM3Service.HttpGetOverride = url => url.EndsWith("/health") ? "{\"status\":\"ok\"}" : slotsBody;
        var first = service.Poll();
        check("A healthy server is running", first.Running);
        check("The busy slot is counted", first.BusySlots == 1 && first.IdleSlots == 1 && first.TotalSlots == 2);
        check("The tokens are counted", first.TotalTokens == 100);
        check("The first request is counted", first.TotalRequests == 1);
        var second = service.Poll();
        check("The same task id counts no new request", second.TotalRequests == 1);
        slotsBody = "[{\"id\":0,\"is_processing\":false},{\"id\":1,\"is_processing\":true,\"id_task\":1,\"n_prompt_tokens_processed\":200}]";
        var third = service.Poll();
        check("A new task id counts a request", third.TotalRequests == 2);
        check("The token counter keeps the highest count", third.TotalTokens == 200);

        // A server that only reports the slot summary (no task id) falls back to the old change detection.
        var plain = new BgeM3Service();
        string plainBody = "[{\"id\":0,\"is_processing\":false}]";
        BgeM3Service.HttpGetOverride = url => url.EndsWith("/health") ? "{\"status\":\"ok\"}" : plainBody;
        var plainFirst = plain.Poll();
        check("A server without task ids counts no request yet", plainFirst.TotalRequests == 0);

        // Poll: a server that is not there.
        BgeM3Service.HttpGetOverride = _ => null;
        var down = service.Poll();
        check("A server without a response is not running", !down.Running);
        check("A server without a response has no slots", down.TotalSlots == 0);
        check("The counters survive a server that went down", down.TotalRequests == 2 && down.TotalTokens == 200);

        // Poll: a server that is still loading the model.
        BgeM3Service.HttpGetOverride = _ => loading;
        var warming = service.Poll();
        check("A loading server is not running yet", !warming.Running);
        check("A loading server is told apart", warming.Loading);

        BgeM3Service.HttpGetOverride = null;
    }
}
