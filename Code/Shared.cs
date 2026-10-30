using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace AirPodsConnect;

internal record Settings(Guid ContainerId, string StereoEndpointId, string BluetoothInstanceId, string BluetoothAddress, string DisplayName)
{
    public void Validate()
    {
        if (ContainerId == Guid.Empty || ContainerId == new Guid("00000000-0000-0000-ffff-ffffffffffff") ||
            string.IsNullOrWhiteSpace(StereoEndpointId) || !StereoEndpointId.StartsWith("{0.0.0.", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(BluetoothAddress) || BluetoothAddress.Length != 12 || !BluetoothAddress.All(Uri.IsHexDigit) ||
            !BluetoothInstanceId.StartsWith("BTHENUM\\DEV_" + BluetoothAddress + "\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid AirPods identity. Run Install.ps1 to configure the paired classic Bluetooth device.");
    }
}
internal record Endpoint(string Id, string Name, Guid ContainerId, uint State, string? FilterId = null)
{
    public bool Active => State == 1;
    public bool Available => State is 1 or 8;
}
internal record Result(int Code, string State, string Message, Endpoint[]? Endpoints = null,
    string? DefaultMultimediaEndpointId = null, bool RequestAttempted = false, string? DriverResult = null)
{
    public DateTime ObservedUtc { get; init; } = DateTime.UtcNow;
}
internal static class Store
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = false, PropertyNameCaseInsensitive = true };
    public static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    public static string Data => Path.Combine(Root, "Data");
    public static string Config => Path.Combine(Data, "settings.json");
    public static string Sid => WindowsIdentity.GetCurrent().User!.Value;
    public static string ShutdownName => @"Local\AirPodsConnect-Exit-" + Sid;
    public static Settings Load(string path)
    {
        var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? throw new ArgumentException("Missing settings.");
        s.Validate();
        return s;
    }
    public static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(Data);
            var path = Path.Combine(Data, "activity.log");
            if (File.Exists(path) && new FileInfo(path).Length > 512_000) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTime.UtcNow:O} {text}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
internal static class Runner
{
    // Both the public CLI and tray supervise children; only the private worker touches COM.
    public static async Task<Result> Run(string executable, IEnumerable<string> arguments, int timeoutMs = 30000)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (string arg in arguments) process.StartInfo.ArgumentList.Add(arg);
        using var deadline = new CancellationTokenSource(timeoutMs);
        try
        {
            process.Start();
            var stdout = ReadBounded(process.StandardOutput, 65536, deadline.Token);
            var stderr = ReadBounded(process.StandardError, 8192, deadline.Token);
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr).WaitAsync(deadline.Token);
            var result = JsonSerializer.Deserialize<Result>(await stdout, Store.Json);
            if (result == null || process.ExitCode != result.Code) throw new IOException("Invalid helper result.");
            return result;
        }
        catch (Exception ex)
        {
            try
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            }
            catch { /* Only this process tree is owned; kernel cancellation is not asserted. */ }
            return new(6, "Unknown", ex is OperationCanceledException ? "Connection check timed out. Wait 30 seconds before retrying." : "Helper failed: " + ex.Message);
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader, int limit, CancellationToken token)
    {
        var text = new StringBuilder();
        char[] buffer = new char[2048];
        int n;
        while ((n = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (text.Length + n > limit) throw new IOException("Helper output exceeded its limit.");
            text.Append(buffer, 0, n);
        }
        return text.ToString();
    }
}
