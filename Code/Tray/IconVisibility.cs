using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace AirPodsConnect;

// Registry candidates are opaque strings. Only the trusted running apphost is
// ever passed to the filesystem resolver; there is no candidate-path resolver.
internal sealed class ExecutableIdentity
{
    readonly string original, resolved;
    internal ExecutableIdentity(string original, string resolved)
    { this.original = original; this.resolved = resolved; }
    internal bool Matches(object? candidate) => candidate is string path &&
        (string.Equals(path, original, StringComparison.Ordinal) || string.Equals(path, resolved, StringComparison.Ordinal));

    internal static ExecutableIdentity? Current() => TryCreate(Environment.ProcessPath, ResolveFinal, IsLocalDrive);
    internal static ExecutableIdentity? TryCreate(string? ownPath, Func<string, string?> resolve, Func<string, bool>? isLocalDrive = null)
    {
        if (!IsDrivePath(ownPath)) return null;
        try
        {
            isLocalDrive ??= IsLocalDrive;
            if (!isLocalDrive(ownPath!)) return null;
            string? final = resolve(ownPath!);
            if (final == null || !final.StartsWith(@"\\?\", StringComparison.Ordinal) || !IsDrivePath(final[4..])) return null;
            string local = final[4..];
            return isLocalDrive(local) ? new(ownPath!, local) : null;
        }
        catch (Exception ex) when (IconVisibility.Expected(ex)) { return null; }
    }
    internal static bool IsDrivePath(string? path) => path is { Length: > 3 } &&
        ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z')) && path[1] == ':' && path[2] == '\\';

    static bool IsLocalDrive(string path) => new DriveInfo(path[..3]).DriveType != DriveType.Network;
    static string? ResolveFinal(string ownPath)
    {
        using var handle = File.OpenHandle(ownPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new StringBuilder(32768);
        uint size = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (size == 0 || size >= buffer.Capacity) return null;
        return buffer.ToString();
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);
}

internal interface IIconRecord : IDisposable
{
    object? RawExecutablePath { get; }
    bool IsPromotedDwordOne { get; }
    void Promote();
}
internal interface IIconRegistry
{
    string[] Names();
    IIconRecord? Open(string name, bool writable);
}
internal sealed class WindowsIconRegistry : IIconRegistry
{
    const string Root = @"Control Panel\NotifyIconSettings";
    public string[] Names()
    {
        using var root = Registry.CurrentUser.OpenSubKey(Root);
        return root?.GetSubKeyNames() ?? Array.Empty<string>();
    }
    public IIconRecord? Open(string name, bool writable)
    {
        using var root = Registry.CurrentUser.OpenSubKey(Root);
        var key = root?.OpenSubKey(name, writable);
        return key == null ? null : new Record(key);
    }
    sealed class Record(RegistryKey key) : IIconRecord
    {
        public object? RawExecutablePath => key.GetValue("ExecutablePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        public bool IsPromotedDwordOne => key.GetValue("IsPromoted", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is int value &&
            value == 1 && key.GetValueKind("IsPromoted") == RegistryValueKind.DWord;
        public void Promote() => key.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
        public void Dispose() => key.Dispose();
    }
}

internal sealed class IconVisibility(IIconRegistry registry, ExecutableIdentity? identity)
{
    internal static bool Expected(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException;
    // Called only in the explicit button handler, synchronously before any await.
    internal bool PromoteExisting()
    {
        if (identity == null) return false;
        int matches = 0;
        try
        {
            foreach (string name in registry.Names())
            {
                using (var read = registry.Open(name, false))
                {
                    if (read == null) return false; // incomplete enumeration
                    if (!identity.Matches(read.RawExecutablePath)) continue;
                }
                using var write = registry.Open(name, true);
                if (write == null || !identity.Matches(write.RawExecutablePath)) return false;
                matches++;
                // The OS has no atomic compare-path-and-write operation. Keep the
                // revalidation adjacent; never roll back a later user preference.
                if (!write.IsPromotedDwordOne) write.Promote();
            }
            return matches > 0;
        }
        catch (Exception ex) when (Expected(ex)) { return false; }
    }
    internal bool CurrentPreferenceSaved()
    {
        if (identity == null) return false;
        int matches = 0;
        try
        {
            foreach (string name in registry.Names())
            {
                using var read = registry.Open(name, false);
                if (read == null) return false;
                if (!identity.Matches(read.RawExecutablePath)) continue;
                matches++;
                if (!read.IsPromotedDwordOne) return false;
            }
            return matches > 0;
        }
        catch (Exception ex) when (Expected(ex)) { return false; }
    }
    internal async Task<bool> VerifyAsync(CancellationToken token, int attempts = 15, int delayMs = 100)
    {
        for (int i = 0; i < attempts; i++)
        {
            // Explorer processes the re-add asynchronously. Give it a turn even
            // when a stale original record was already promoted before refresh.
            await Task.Delay(delayMs, token);
            token.ThrowIfCancellationRequested();
            if (CurrentPreferenceSaved()) return true;
        }
        token.ThrowIfCancellationRequested();
        return false;
    }
}
