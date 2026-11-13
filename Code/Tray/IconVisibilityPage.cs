using System.Runtime.InteropServices;

namespace AirPodsConnect;

internal sealed class IconVisibilityPage : Form
{
    readonly IconVisibility visibility;
    readonly Func<bool> trayAlive;
    readonly Action refreshIcon;
    readonly Action? shown;
    readonly CancellationTokenSource lifetime = new();
    readonly Font headingFont = new("Segoe UI", 17, FontStyle.Bold);
    readonly Font bodyFont = new("Segoe UI", 10);
    readonly Button primary = new() { Text = "Show my AirPods icon", AutoSize = true, Padding = new Padding(12, 6, 12, 6) };
    readonly Label feedback = new() { AutoSize = false, Height = 58, Dock = DockStyle.Fill, Text = "Choose once to save Windows' visibility preference for this app." };
    readonly Button done = new() { Text = "Later", AutoSize = true };
    bool closing;
    internal IconVisibilityPage(IconVisibility visibility, Func<bool> trayAlive, Action refreshIcon, Action? shown = null)
    {
        this.visibility = visibility; this.trayAlive = trayAlive; this.refreshIcon = refreshIcon; this.shown = shown;
        Text = "AirPods Connect"; ClientSize = new Size(430, 225); StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; Font = bodyFont;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.Controls.Add(new Label { Text = "Keep AirPods visible", Font = headingFont, AutoSize = true }, 0, 0);
        layout.Controls.Add(primary, 0, 1); layout.Controls.Add(feedback, 0, 2);
        layout.Controls.Add(done, 0, 3); Controls.Add(layout);
        primary.Click += async (_, _) => await SavePreferenceAsync();
        done.Click += (_, _) => Close();
    }
    internal bool NativeVisible => IsHandleCreated && IsWindowVisible(Handle);
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (closing || !trayAlive()) return;
        // A normal hidden Startup shortcut (and CIM ShowWindow=0) can suppress
        // Windows' first ShowWindow call even though WinForms raises Shown.
        // This explicit page presentation happens after that first call.
        ShowWindow(Handle, 5); // SW_SHOW; no connection or preference changes
        if (NativeVisible) shown?.Invoke();
    }
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    bool Active(CancellationToken token) => !token.IsCancellationRequested && !closing && !IsDisposed && !Disposing && trayAlive();
    internal async Task SavePreferenceAsync()
    {
        CancellationToken token = lifetime.Token; // belongs to this page, never a replacement page
        if (!Active(token) || !primary.Enabled) return;
        primary.Enabled = false; feedback.Text = "Saving preference…";
        try
        {
            // No await before these operations: closing/exit cannot interleave on the UI thread.
            bool changed = visibility.PromoteExisting();
            if (!Active(token)) return;
            if (changed) refreshIcon();
            bool saved = changed && await visibility.VerifyAsync(token);
            if (!Active(token)) return;
            feedback.Text = saved ? "Preference saved. Windows controls where the icon appears." :
                "Windows couldn't confirm this preference. Wait a moment, then try again.";
            primary.Text = saved ? "Show my AirPods icon" : "Retry: show my AirPods icon";
            done.Text = saved ? "Done" : "Later";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (IconVisibility.Expected(ex))
        {
            if (Active(token)) { feedback.Text = "Windows couldn't confirm this preference. Please retry."; primary.Text = "Retry: show my AirPods icon"; }
        }
        finally { if (Active(token)) primary.Enabled = true; }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    { closing = true; lifetime.Cancel(); base.OnFormClosing(e); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !closing) { closing = true; lifetime.Cancel(); }
        base.Dispose(disposing);
        if (disposing) { lifetime.Dispose(); headingFont.Dispose(); bodyFont.Dispose(); }
    }
}

internal static class TrayLifecycle
{
    internal static void PostExit(Control dispatcher, Func<bool> exiting, Action exit, Action<Action>? post = null)
    {
        if (exiting() || dispatcher.IsDisposed || dispatcher.Disposing || !dispatcher.IsHandleCreated) return;
        try { if (post == null) dispatcher.BeginInvoke(exit); else post(exit); }
        catch (InvalidOperationException) when (exiting() || dispatcher.IsDisposed || dispatcher.Disposing || !dispatcher.IsHandleCreated) { }
    }
}
