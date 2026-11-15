using System.IO;
using AxDeck.Native;

// The live check of the BGE-M3 service: it starts the real llama-server with the real model, reads the live status
// and stops it again. It is not part of the default run, because it loads a ~450 MiB model and needs llama.cpp
// installed: "--only bge-live" asks for it on a machine that has both.
internal static class BgeM3Live
{
    public static void Run(Action<string, bool> check)
    {
        var server = BgeM3Service.ResolveServerPath();
        check("llama-server.exe is found", server is not null);
        check("the bge-m3 model file is found", File.Exists(BgeM3Service.ModelPath));
        if (server is null || !File.Exists(BgeM3Service.ModelPath)) return;

        var service = new BgeM3Service();
        var (started, detail) = service.Start();
        check($"the server starts ({detail})", started);
        Console.WriteLine($"  raw /health: {BgeM3Service.ReadRaw("/health")}");
        Console.WriteLine($"  raw /slots:  {BgeM3Service.ReadRaw("/slots")}");
        var snapshot = service.Poll();
        Console.WriteLine($"  snapshot: running={snapshot.Running} loading={snapshot.Loading} detail={snapshot.Detail} slots={snapshot.TotalSlots}");
        check($"the server reports healthy ({snapshot.Detail})", snapshot.Running);
        check($"the slots are reported ({snapshot.TotalSlots})", snapshot.TotalSlots > 0);
        check("the busy and the idle slots add up", snapshot.BusySlots + snapshot.IdleSlots == snapshot.TotalSlots);
        check("the server log was written", BgeM3Service.ReadLogTail(5).Count > 0);
        var (stopped, stopDetail) = service.Stop();
        check($"the server stops ({stopDetail})", stopped);
    }
}
