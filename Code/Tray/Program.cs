using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AirPodsConnect;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--exit"))
        {
            try { using var stop = EventWaitHandle.OpenExisting(Store.ShutdownName); stop.Set(); } catch (WaitHandleCannotBeOpenedException) { }
            return;
        }
        using var mutex = new Mutex(false, @"Local\AirPodsConnect-Tray-" + Store.Sid);
        bool acquired;
        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return;
        try { ApplicationConfiguration.Initialize(); Application.Run(new Tray()); }
        finally { mutex.ReleaseMutex(); }
    }
}
internal sealed class Tray : ApplicationContext
{
    readonly NotifyIcon icon;
    readonly ContextMenuStrip menu = new();
    readonly ToolStripMenuItem status = new("Checking AirPods…");
    readonly ToolStripMenuItem connect = new("Connect AirPods");
    readonly ToolStripMenuItem disconnect = new("Disconnect audio");
    readonly System.Windows.Forms.Timer timer = new() { Interval = 20000 };
    readonly EventWaitHandle stop = new(false, EventResetMode.ManualReset, Store.ShutdownName);
    readonly Control dispatcher = new();
    readonly RegisteredWaitHandle stopWait;
    readonly Dictionary<string, Icon> icons = new();
    bool busy, exiting;
    DateTime retryAfter;
    string lastState = "";
    Result? latest;
    Form? details;
    TextBox? detailsText;
    readonly string helper = Path.Combine(AppContext.BaseDirectory, "AirPodsHelper.exe");
    public Tray()
    {
        dispatcher.CreateControl();
        icons["idle"] = MakeIcon(Color.FromArgb(190, 198, 210));
        icons["busy"] = MakeIcon(Color.FromArgb(255, 190, 65));
        icons["connected"] = MakeIcon(Color.FromArgb(55, 218, 137));
        icons["error"] = MakeIcon(Color.FromArgb(255, 100, 100));
        icon = new NotifyIcon { Icon = icons["idle"], Text = "AirPods Connect · checking status", Visible = true, ContextMenuStrip = menu };
        status.Click += (_, _) => ShowDetails();
        connect.Click += async (_, _) => await Act("--connect");
        disconnect.Click += async (_, _) => await Act("--disconnect");
        menu.Items.AddRange(new ToolStripItem[] { status, new ToolStripSeparator(), connect, disconnect,
            new ToolStripSeparator(), new ToolStripMenuItem("Connection details", null, (_, _) => ShowDetails()),
            new ToolStripMenuItem("Bluetooth settings", null, (_, _) => Open("ms-settings:bluetooth")),
            new ToolStripMenuItem("Sound output settings", null, (_, _) => Open("ms-settings:sound")),
            new ToolStripSeparator(), new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()) });
        icon.MouseClick += async (_, e) => { if (e.Button == MouseButtons.Left) await Act("--connect"); };
        timer.Tick += async (_, _) => await Refresh();
        stopWait = ThreadPool.RegisterWaitForSingleObject(stop, (_, _) => dispatcher.BeginInvoke((Action)ExitThread), null, -1, true);
        timer.Start();
        Store.Log("Tray started; startup does not connect.");
        _ = Refresh();
    }
    static void Open(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    async Task Refresh()
    {
        if (busy || exiting) return;
        busy = true;
        try { var r = await Runner.Run(helper, new[] { "--status" }, 7000); if (!exiting) Apply(r); }
        finally { busy = false; }
    }
    async Task Act(string operation)
    {
        if (busy || exiting) return;
        if (DateTime.UtcNow < retryAfter) { Balloon("Please wait", "The previous connection attempt is still settling. Try again shortly."); return; }
        busy = true; timer.Stop(); connect.Enabled = disconnect.Enabled = false;
        icon.Icon = icons["busy"]; icon.Text = "AirPods Connect · " + (operation == "--connect" ? "connecting…" : "disconnecting…");
        status.Text = operation == "--connect" ? "Connecting AirPods…" : "Disconnecting audio…";
        Store.Log("Click action " + operation);
        try
        {
            Result r = await Runner.Run(helper, new[] { operation }, 30000);
            if (exiting) return;
            if (r.Code is 6 or 7) retryAfter = DateTime.UtcNow.AddSeconds(30);
            Apply(r);
            Store.Log("Action result " + System.Text.Json.JsonSerializer.Serialize(r, Store.Json));
            Balloon(r.Code == 0 ? "AirPods Connect" : "AirPods connection", r.Message);
        }
        finally { busy = false; connect.Enabled = disconnect.Enabled = true; if (!exiting) timer.Start(); }
    }
    void Apply(Result r)
    {
        latest = r;
        string key = r.Code != 0 ? "error" : r.State == "Connected" ? "connected" : r.State == "CallsOnly" ? "busy" : "idle";
        icon.Icon = icons[key];
        string tip = "AirPods · " + (r.Code != 0 ? "connection unavailable · right-click for details" : r.State == "Connected" ? "connected" : r.State == "CallsOnly" ? "calls only · click to connect audio" : "click to connect");
        icon.Text = tip[..Math.Min(tip.Length, 63)];
        status.Text = r.Message;
        if (r.State != lastState) { Store.Log("State " + r.State + " · " + r.Message); lastState = r.State; }
        if (detailsText != null && !detailsText.IsDisposed) detailsText.Text = Describe();
    }
    void Balloon(string title, string text) { icon.BalloonTipTitle = title; icon.BalloonTipText = text; icon.ShowBalloonTip(2500); }
    string Describe() => "AirPods Connect\r\n\r\nLeft-click the AirPods tray icon to connect.\r\n\r\n" +
        (latest == null ? "Checking…" : latest.Message + "\r\n\r\nLast observed: " + latest.ObservedUtc.ToLocalTime().ToString("G") +
        "\r\nState: " + latest.State + "\r\nDefault multimedia output is AirPods: " +
        (latest.Endpoints?.Any(e => e.Id == latest.DefaultMultimediaEndpointId) == true ? "Yes" : "No") +
        "\r\n\r\n" + string.Join("\r\n", latest.Endpoints?.Select(e => e.Name + " · state " + e.State) ?? Array.Empty<string>())) +
        "\r\n\r\nGray: disconnected  •  Amber: connecting/calls only\r\nGreen: stereo audio connected  •  Red: error/unknown\r\n\r\n" +
        "Startup opens the icon; it does not auto-connect.\r\nProject: " + Store.Root;
    void ShowDetails()
    {
        if (details != null && !details.IsDisposed) { details.Activate(); return; }
        details = new Form { Text = "AirPods Connect — Connection details", Width = 580, Height = 440, StartPosition = FormStartPosition.CenterScreen, Icon = icons["idle"] };
        detailsText = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 11), Text = Describe() };
        details.Controls.Add(detailsText); details.Show();
    }
    protected override void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true; timer.Stop(); timer.Dispose(); stopWait.Unregister(null); stop.Dispose();
        details?.Close(); icon.Visible = false; icon.Dispose(); menu.Dispose();
        foreach (var value in icons.Values) value.Dispose(); dispatcher.Dispose();
        Store.Log("Tray exited cleanly."); base.ExitThreadCore();
    }
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    static Icon MakeIcon(Color accent)
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var white = new SolidBrush(Color.White); using var dark = new SolidBrush(Color.FromArgb(50, 58, 68)); using var color = new SolidBrush(accent);
        // Two short-stem earbuds remain recognizable at 16px on the dark Windows tray.
        g.FillEllipse(white, 1, 2, 14, 13); g.FillEllipse(white, 17, 2, 14, 13);
        g.FillRectangle(white, 8, 10, 5, 17); g.FillRectangle(white, 19, 10, 5, 17);
        g.FillEllipse(white, 8, 24, 5, 5); g.FillEllipse(white, 19, 24, 5, 5);
        g.FillEllipse(dark, 3, 6, 4, 6); g.FillEllipse(dark, 25, 6, 4, 6);
        g.FillEllipse(color, 11, 20, 11, 11);
        IntPtr h = bmp.GetHicon(); try { using var temporary = Icon.FromHandle(h); return (Icon)temporary.Clone(); } finally { DestroyIcon(h); }
    }
}
