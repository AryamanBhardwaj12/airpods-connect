using System.Reflection;

namespace AirPodsConnect;

internal static class IconVisibilityTests
{
    const string Own = @"C:\Apps\AirPodsConnect.exe", Resolved = @"C:\Real\AirPodsConnect.exe";
    static int count;
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    static IconVisibility Service(FakeRegistry registry) => new(registry, new ExecutableIdentity(Own, Resolved));
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--hidden-page-probe")
        {
            var records = new FakeRegistry(); records.Add("own", Own, 0);
            IconVisibilityPage? page = null;
            using var close = new System.Windows.Forms.Timer { Interval = 250 };
            using (page = new IconVisibilityPage(Service(records), () => true, () => throw new Exception("No button click authorized"), () =>
            {
                File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(new { NativeVisible=page!.NativeVisible, Writes=records.Writes.Count, Runtime=Environment.Version.ToString() }));
                close.Start();
            }))
            {
                close.Tick += (_, _) => { close.Stop(); page.Close(); };
                Application.Run(page);
            }
            return File.Exists(args[1]) ? 0 : 1;
        }
        using var dispatcher = new Control(); dispatcher.CreateControl();
        var tests = Run();
        while (!tests.IsCompleted) { Application.DoEvents(); Thread.Sleep(1); }
        try { tests.GetAwaiter().GetResult(); Console.WriteLine($"{count} assertions passed (.NET {Environment.Version}; synthetic, no live registry writes/Bluetooth requests)."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static async Task Run()
    {
        int resolves = 0;
        var identity = ExecutableIdentity.TryCreate(Own, path => { resolves++; Check(path == Own, "only trusted own path reaches resolver"); return @"\\?\" + Resolved; });
        Check(identity!.Matches(Own) && identity.Matches(Resolved) && resolves == 1, "original and resolved exact identity");
        foreach (object? other in new object?[] { Own.ToLowerInvariant(), @"C:\APPS~1\AIRPOD~1.EXE", @"\\host\share\AirPodsConnect.exe", @"%LOCALAPPDATA%\AirPodsConnect.exe", @"\\?\C:\Apps\AirPodsConnect.exe", @"{GUID}\AirPodsConnect.exe", 7, null })
            Check(!identity.Matches(other), "opaque nonmatching candidate " + other);
        foreach (string own in new[] { @"\\host\share\app.exe", @"\\?\C:\app.exe", @"\Device\app.exe", @"\\?\Volume{abc}\app.exe", "relative.exe" })
            Check(ExecutableIdentity.TryCreate(own, _ => throw new Exception("Candidate filesystem access")) == null, "reject unsupported trusted path " + own);
        Check(ExecutableIdentity.TryCreate(Own, _ => null) == null, "network/unresolvable own identity fails safe");
        Check(ExecutableIdentity.TryCreate(Own, _ => @"\\host\share\app.exe") == null, "final UNC rejected");
        Check(ExecutableIdentity.TryCreate(Own, _ => @"\\?\Volume{abc}\app.exe") == null, "final volume GUID rejected");
        Check(ExecutableIdentity.TryCreate(Own, _ => throw new Exception("Must not resolve network drive"), _ => false) == null, "mapped network original rejected before filesystem resolve");
        Check(ExecutableIdentity.TryCreate(Own, _ => @"\\?\D:\app.exe", path => path[0] != 'D') == null, "mapped network resolved drive rejected");
        Check(ExecutableIdentity.TryCreate(Own, _ => @"\Device\app.exe") == null, "final device path rejected");
        var registry = new FakeRegistry();
        Check(!Service(registry).PromoteExisting() && !Service(registry).CurrentPreferenceSaved(), "no records Retry");
        registry.Add("own", Own, 0); registry.Add("resolved", Resolved, null);
        registry.Add("foreign", Own.ToLowerInvariant(), 0); registry.Add("env", @"%LOCALAPPDATA%\AirPodsConnect.exe", 0);
        registry.Add("unc", @"\\server\share\AirPodsConnect.exe", 0); registry.Add("nonstring", 8, 0);
        Check(Service(registry).PromoteExisting(), "promote all exact own records including missing value");
        Check(registry.Writes.SequenceEqual(new[] { "own", "resolved" }), "only exact own IsPromoted writes; no candidate filesystem API");
        Check(Service(registry).CurrentPreferenceSaved(), "all exact current records DWORD1");
        Check(Service(registry).PromoteExisting() && registry.Writes.Count == 2, "already DWORD1 is no-op");
        registry.Values["own"].Value = "1";
        Check(!Service(registry).CurrentPreferenceSaved(), "string1 does not verify");
        Check(Service(registry).PromoteExisting() && registry.Values["own"].Value is 1, "wrong-type value replaced by DWORD1");
        registry.Values["own"].Value = 1L;
        Check(!Service(registry).CurrentPreferenceSaved(), "QWORD1 does not verify");
        registry.Values["own"].Value = 1;
        var reassigned = new FakeRegistry(); reassigned.Add("own", Own, 0);
        reassigned.OnOpen = (name, write) => { if (write) reassigned.Values[name].Path = "foreign"; };
        Check(!Service(reassigned).PromoteExisting() && reassigned.Writes.Count == 0, "writable reopen revalidates reassigned path");
        var removed = new FakeRegistry(); removed.Add("own", Own, 0);
        removed.OnOpen = (name, write) => { if (write) removed.Values.Remove(name); };
        Check(!Service(removed).PromoteExisting() && removed.Writes.Count == 0, "removed between opens fails safely");
        var denied = new FakeRegistry(); denied.Add("own", Own, 0);
        denied.OnOpen = (_, write) => { if (write) throw new UnauthorizedAccessException(); };
        Check(!Service(denied).PromoteExisting(), "write denial Retry");
        denied.OnNames = () => throw new UnauthorizedAccessException();
        Check(!Service(denied).CurrentPreferenceSaved(), "incomplete denied enumeration cannot succeed");
        var incomplete = new FakeRegistry(); incomplete.Add("own", Own, 1); incomplete.Add("vanished", "foreign", 0);
        incomplete.OnOpen = (name, _) => { if (name == "vanished") incomplete.Values.Remove(name); };
        Check(!Service(incomplete).CurrentPreferenceSaved(), "disappeared enumerated record is incomplete");
        var churn = new FakeRegistry(); churn.Add("old", Own, 1); churn.Add("surviving", Resolved, 1);
        Check(Service(churn).CurrentPreferenceSaved(), "initial mixed current matches verify");
        churn.Values["old"].Path = "foreign"; churn.Values["old"].Value = 0;
        Check(Service(churn).CurrentPreferenceSaved(), "reassigned old record plus promoted survivor succeeds");
        churn.Values.Remove("old");
        Check(Service(churn).CurrentPreferenceSaved(), "removed old record plus promoted survivor succeeds");
        churn.Add("new", Own, 0);
        Check(!await Service(churn).VerifyAsync(CancellationToken.None, 3, 1) && churn.Writes.Count == 0, "new unpromoted record prevents success; verification never re-promotes");
        churn.Values.Remove("new"); churn.Values.Remove("surviving");
        Check(!Service(churn).CurrentPreferenceSaved(), "zero current matches fails");
        var deferred = new FakeRegistry(); deferred.Add("old", Own, 1);
        int readbacks = 0; deferred.OnNames = () => readbacks++;
        var deferredVerification = Service(deferred).VerifyAsync(CancellationToken.None, 3, 10);
        Check(!deferredVerification.IsCompleted && readbacks == 0, "first readback waits for deferred Explorer refresh");
        deferred.Add("deferred-new", Own, 0);
        Check(!await deferredVerification && readbacks == 3 && deferred.Writes.Count == 0, "deferred new unpromoted record fails all bounded read-only attempts");
        using (var cancel = new CancellationTokenSource())
        {
            var pending = Service(churn).VerifyAsync(cancel.Token); cancel.Cancel();
            try { await pending; throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { Check(true, "verification cancellation during delay"); }
        }
        Check(!new IconVisibility(registry, null).PromoteExisting(), "unsupported own identity never writes");
        // Real WinForms pages, fake registry: opening/dismissal never promotes.
        int shown = 0, refreshes = 0;
        var pages = new FakeRegistry(); pages.Add("own", Own, 0);
        using (var page = new IconVisibilityPage(Service(pages), () => true, () => refreshes++, () => shown++))
        {
            Check(shown == 0 && pages.Writes.Count == 0, "construct page neither marks shown nor promotes");
            page.Show(); Application.DoEvents(); Check(shown == 1 && pages.Writes.Count == 0, "actual shown notification without promotion");
            await page.SavePreferenceAsync();
            Check(pages.Writes.Count == 1 && refreshes == 1 && Field<Label>(page, "feedback").Text.StartsWith("Preference saved"), "explicit click promotes refreshes and verifies");
            Check(Field<Button>(page, "done").Text == "Done", "saved page offers Done"); page.Close();
        }
        var pendingRecords = new FakeRegistry(); pendingRecords.Add("old", Own, 0);
        bool alive = true;
        using (var a = new IconVisibilityPage(Service(pendingRecords), () => alive, () => pendingRecords.Add("readded", Own, 0)))
        {
            a.Show(); var pending = a.SavePreferenceAsync();
            Check(!pending.IsCompleted && pendingRecords.Writes.Count == 1, "mutation and same-icon refresh complete before await");
            a.Close();
            using var b = new IconVisibilityPage(Service(pendingRecords), () => true, () => { }); b.Show();
            string initialB = Field<Label>(b, "feedback").Text;
            await pending;
            Check(Field<Label>(b, "feedback").Text == initialB && Field<Button>(b, "primary").Enabled, "close A reopen B isolates success/error/finally callbacks");
            Check(pendingRecords.Writes.Count == 1, "close during await causes no later writes or refresh"); b.Close();
        }
        using (var page = new IconVisibilityPage(Service(pendingRecords), () => alive, () => { }))
        {
            page.Show(); pendingRecords.Values["readded"].Value = 0;
            // Re-add after refresh to ensure a bounded pending read loop.
            using var exitPage = new IconVisibilityPage(Service(pendingRecords), () => alive, () => pendingRecords.Add("latest", Own, 0)); exitPage.Show();
            var pending = exitPage.SavePreferenceAsync(); alive = false; exitPage.Close(); await pending;
            Check(true, "tray exit cancels page verification safely"); page.Close();
        }
        using (var missingPage = new IconVisibilityPage(Service(new FakeRegistry()), () => true, () => throw new Exception("Should not refresh")))
        {
            missingPage.Show(); await missingPage.SavePreferenceAsync();
            Check(Field<Button>(missingPage, "primary").Text.StartsWith("Retry") && Field<Button>(missingPage, "primary").Enabled, "missing records expose explicit Retry"); missingPage.Close();
        }
        using (var disposed = new Control())
        {
            disposed.CreateControl(); disposed.Dispose();
            TrayLifecycle.PostExit(disposed, () => false, () => throw new Exception("Disposed callback dispatched"));
            Check(true, "disposed dispatcher callback contained");
        }
        using (var live = new Control())
        {
            live.CreateControl(); bool called = false;
            TrayLifecycle.PostExit(live, () => true, () => called = true);
            Application.DoEvents(); Check(!called, "shutdown signal prevents callback post");
        }
        using (var raced = new Control())
        {
            raced.CreateControl();
            TrayLifecycle.PostExit(raced, () => false, () => { }, action => { raced.Dispose(); raced.BeginInvoke(action); });
            Check(true, "real BeginInvoke disposal race exception contained inside callback");
        }
        using (var live = new Control())
        {
            live.CreateControl(); bool propagated = false;
            try { TrayLifecycle.PostExit(live, () => false, () => { }, _ => throw new InvalidOperationException("unrelated")); }
            catch (InvalidOperationException) { propagated = true; }
            Check(propagated, "unrelated live-dispatcher failure is not swallowed");
        }
        // Real Tray action flow on .NET 8 with an injected synthetic helper only.
        var completion = new TaskCompletionSource<Result>();
        using (var tray = new Tray(_ => completion.Task, false))
        {
            var action = (Task)typeof(Tray).GetMethod("Act", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(tray, new object[] { "--connect" })!;
            Check(Field<bool>(tray, "busy"), "synthetic helper action pending");
            typeof(Tray).GetMethod("ExitThreadCore", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(tray, null);
            completion.SetResult(new Result(0, "Connected", "synthetic")); await action;
            Check(!Field<bool>(tray, "busy") && !Field<System.Windows.Forms.Timer>(tray, "timer").Enabled, "late helper completion clears busy without timer/control restart after exit");
        }
        Check(ExecutableIdentity.Current() != null, "real running .NET8 local apphost resolves (read-only)");
    }
    sealed class FakeRegistry : IIconRegistry
    {
        internal sealed class Entry(object? path, object? value) { internal object? Path = path, Value = value; }
        internal readonly Dictionary<string, Entry> Values = new();
        internal readonly List<string> Writes = new();
        internal Action<string, bool>? OnOpen;
        internal Action? OnNames;
        internal void Add(string name, object? path, object? value) => Values[name] = new(path, value);
        public string[] Names() { OnNames?.Invoke(); return Values.Keys.ToArray(); }
        public IIconRecord? Open(string name, bool writable)
        { OnOpen?.Invoke(name, writable); return Values.TryGetValue(name, out var value) ? new Record(this, name, value, writable) : null; }
        sealed class Record(FakeRegistry registry, string name, Entry value, bool writable) : IIconRecord
        {
            public object? RawExecutablePath => value.Path;
            public bool IsPromotedDwordOne => value.Value is int n && n == 1;
            public void Promote() { if (!writable) throw new Exception("Read-only mutation"); registry.Writes.Add(name); value.Value = 1; }
            public void Dispose() { }
        }
    }
}
