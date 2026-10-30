using System.Runtime.InteropServices;
using System.Text.Json;

namespace AirPodsConnect;

internal static class Program
{
    [MTAThread]
    static int Main(string[] args)
    {
        Result result;
        try
        {
            string config = Store.Config;
            int ci = Array.IndexOf(args, "--config");
            if (ci >= 0) config = args[ci + 1];
            string mode = args.FirstOrDefault(x => new[] { "--list", "--status", "--connect", "--disconnect", "--self-test", "--fake-delay" }.Contains(x)) ?? "--status";
            if (mode == "--self-test") result = SelfTest();
            else if (!args.Contains("--worker"))
                result = Runner.Run(Environment.ProcessPath!, new[] { mode, "--config", config, "--worker" }).GetAwaiter().GetResult();
            else if (mode == "--fake-delay") { Thread.Sleep(10000); result = new(0, "Test", "Delayed worker finished."); }
            else result = Work(mode, config);
        }
        catch (Exception ex) { result = new(ex is ArgumentException or IOException or JsonException ? 2 : 5, "Error", ex.Message); }
        Console.WriteLine(JsonSerializer.Serialize(result, Store.Json));
        return result.Code;
    }
    static Result Work(string mode, string config)
    {
        using var audio = new Audio();
        if (mode == "--list" && !File.Exists(config)) return new(0, "Inventory", "Read-only render endpoints", audio.List(resolve: true));
        Settings settings = Store.Load(config);
        if (mode == "--list") return audio.Status(settings, resolve: true) with { Message = "Read-only target endpoints and audio filter paths" };
        if (mode == "--status") return audio.Status(settings);
        using var gate = new Mutex(false, @"Local\AirPodsConnect-Operation-" + Store.Sid + "-" + settings.ContainerId.ToString("N"));
        bool acquired;
        try { acquired = gate.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return new(7, "Busy", "An AirPods operation is already in progress.");
        try
        {
            var before = audio.Status(settings, resolve: true);
            if (before.Code != 0) return before;
            bool disconnect = mode == "--disconnect";
            if (!disconnect && before.State == "Connected") return before with { Message = "AirPods already connected · no reconnect request needed" };
            if (disconnect && before.Endpoints!.All(e => !e.Active)) return before with { Message = "AirPods audio is already disconnected" };
            string cooldown = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(config))!, "operation-cooldown.txt");
            if (File.Exists(cooldown) && long.TryParse(File.ReadAllText(cooldown), out long ticks) && DateTime.UtcNow.Ticks < ticks)
                return before with { Code = 7, Message = "Previous connection outcome is still settling. Wait 30 seconds before retrying." };
            Directory.CreateDirectory(Path.GetDirectoryName(cooldown)!);
            // Persist BEFORE the driver call: survives process death and public CLI cancellation.
            File.WriteAllText(cooldown, DateTime.UtcNow.AddSeconds(60).Ticks.ToString());
            bool attempted = false;
            string hrText = "";
            try
            {
                var targets = disconnect
                    ? before.Endpoints!.Where(x => x.Available && x.FilterId != null).GroupBy(x => x.FilterId, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToArray()
                    : new[] { Audio.SelectStereo(settings, before.Endpoints!) };
                foreach (var endpoint in targets)
                {
                    attempted = true;
                    int hr = audio.Send(settings, endpoint, disconnect);
                    hrText += $"{endpoint.Id}: 0x{hr:X8}; ";
                    if (hr < 0)
                    {
                        int code = hr is unchecked((int)0x80070490) or unchecked((int)0x80070492) or unchecked((int)0x80070032) ? 4 : 5;
                        return audio.Status(settings) with { Code = code, Message = "Windows could not request the audio connection. " + hrText, RequestAttempted = attempted, DriverResult = hrText };
                    }
                }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                Result after;
                do
                {
                    Thread.Sleep(400);
                    after = audio.Status(settings);
                    if ((!disconnect && after.State == "Connected") || (disconnect && after.Endpoints != null && after.Endpoints.All(e => !e.Active)))
                    {
                        File.WriteAllText(cooldown, "0");
                        return after with { Message = disconnect ? "AirPods audio disconnected" : after.Message, RequestAttempted = attempted, DriverResult = hrText };
                    }
                } while (clock.Elapsed < TimeSpan.FromSeconds(15));
                return after with { Code = 6, Message = disconnect ? "Some AirPods audio is still connected." : "AirPods did not connect. Keep them awake and nearby, then retry in 30 seconds.", RequestAttempted = attempted, DriverResult = hrText };
            }
            finally
            {
                // A successful observation clears the file; failures keep a finite cooldown.
                if (File.ReadAllText(cooldown) != "0") File.WriteAllText(cooldown, DateTime.UtcNow.AddSeconds(25).Ticks.ToString());
            }
        }
        finally { gate.ReleaseMutex(); }
    }
    static Result SelfTest()
    {
        int checks = 0;
        void Assert(bool value) { checks++; if (!value) throw new Exception("Self-test failed at assertion " + checks); }
        Assert(IntPtr.Size == 8); Assert(Marshal.SizeOf<KsProperty>() == 24); Assert(Marshal.SizeOf<PropertyKey>() == 20);
        Assert(Marshal.SizeOf<PropVariant>() == 24); Assert(Marshal.OffsetOf<PropVariant>(nameof(PropVariant.Pointer)).ToInt32() == 8);
        var c = Guid.NewGuid(); var s = new Settings(c, "{0.0.0.00000000}.{test}", "BTHENUM\\DEV_AABBCCDDEEFF\\test", "AABBCCDDEEFF", "test");
        var ep = new Endpoint(s.StereoEndpointId, "Localized name", c, 8);
        Assert(Audio.SelectStereo(s, new[] { ep, ep with { ContainerId = Guid.NewGuid(), State = 1 } }) == ep);
        foreach (var bad in new[] { Array.Empty<Endpoint>(), new[] { ep, ep }, new[] { ep with { State = 2 } }, new[] { ep with { State = 4 } } })
        { bool rejected = false; try { Audio.SelectStereo(s, bad); } catch (ArgumentException) { rejected = true; } Assert(rejected); }
        bool invalid = false; try { (s with { ContainerId = Guid.Empty }).Validate(); } catch (ArgumentException) { invalid = true; } Assert(invalid);
        var delayed = Runner.Run(Environment.ProcessPath!, new[] { "--worker", "--fake-delay" }, 100).GetAwaiter().GetResult();
        Assert(delayed.Code == 6); Assert(!delayed.RequestAttempted);
        return new(0, "TestsPassed", $"{checks} assertions passed: identity, endpoint selection, disabled/missing/ambiguous targets, ABI and bounded worker timeout. No Bluetooth requests sent.");
    }
}
