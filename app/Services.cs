using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

enum SoftwareState { NotInstalled, Stopped, Running }

// Logitech's other software, which can take over a device's lighting (docs/services.md). Stop ends its programs, stops its services
// and keeps them from starting with Windows: the services set to start only when asked (demand), the startup entries turned off the
// way Task Manager does (StartupApproved, the entry itself kept). Start puts back what the installers set (automatic, entries on),
// so G-Lite has nothing to remember and Task Manager or Services can undo it too. Services and the all-users startup entries need an
// administrator: an elevated G-Lite (--services) does them, one Windows prompt per click. Names seen on a PC with both installed
// (2026-11-23).
class LogitechSoftware
{
    public string Id, Name, Detail;
    public string[] Services = new string[0], ProcessPrefixes = new string[0], StartupPrograms = new string[0];

    public static readonly LogitechSoftware[] All =
    {
        new LogitechSoftware
        {
            Id = "ghub", Name = "G HUB", Detail = "lghub.exe and the LGHUB Updater Service",
            // logitechg_discord.exe, its Discord link, runs alongside it.
            Services = new[] { "LGHUBUpdaterService" }, ProcessPrefixes = new[] { "lghub", "logitechg_" },
            StartupPrograms = new[] { "lghub" }
        },
        new LogitechSoftware
        {
            Id = "lgs", Name = "Logitech Gaming Software", Detail = "LCore.exe and the Logitech Gaming Registry Service",
            Services = new[] { "LogiRegistryService" }, ProcessPrefixes = new[] { "LCore" }, StartupPrograms = new[] { "LCore.exe" }
        },
        new LogitechSoftware
        {
            // No Run entry or scheduled task: its service starts logioptionsplus_agent.exe (seen 2026-11-26, Options+ for an MX
            // Mechanical Mini). The agent sets the MX devices' settings again when they connect, over G-Lite's.
            Id = "optionsplus", Name = "Logi Options+", Detail = "logioptionsplus.exe, its agent and its service",
            Services = new[] { "OptionsPlusUpdaterService" }, ProcessPrefixes = new[] { "logioptionsplus" }
        },
        new LogitechSoftware
        {
            Id = "lamparray", Name = "Logitech LampArray Service", Detail = "Hands Logitech devices to Windows Dynamic Lighting",
            Services = new[] { "logi_lamparray_service" }
        },
    };

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public class StartupEntry
    {
        public RegistryHive Hive;
        public RegistryView RunView;
        public string Approved, Name;
        public bool Machine { get { return Hive == RegistryHive.LocalMachine; } }
    }

    public static string[] RunningProcesses() { return Process.GetProcesses().Select(NameAndDispose).ToArray(); }

    static string NameAndDispose(Process process)
    {
        try { return process.ProcessName; }
        catch (InvalidOperationException) { return ""; }
        finally { process.Dispose(); }
    }

    public SoftwareState State(IEnumerable<string> running)
    {
        bool installed = false, active = false;
        foreach (var name in Services)
        {
            var status = ServiceStatus(name);
            if (status == null) continue;
            installed = true;
            if (status != ServiceControllerStatus.Stopped) active = true;
        }
        if (running.Any(Ours)) installed = active = true;
        if (!installed && StartupEntries().Any()) installed = true;
        return active ? SoftwareState.Running : installed ? SoftwareState.Stopped : SoftwareState.NotInstalled;
    }

    bool Ours(string processName)
    {
        return ProcessPrefixes.Any(prefix => processName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    // Null when it isn't installed.
    static ServiceControllerStatus? ServiceStatus(string name)
    {
        try { using (var service = new ServiceController(name)) return service.Status; }
        catch (InvalidOperationException) { return null; }
    }

    public static bool Mentions(string command, string program)
    {
        return command != null && command.IndexOf(program, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // The Run entries that start one of its programs: the user's, and the all-users ones in both registry views.
    public List<StartupEntry> StartupEntries()
    {
        var places = new[]
        {
            new StartupEntry { Hive = RegistryHive.CurrentUser, RunView = RegistryView.Default, Approved = ApprovedKey + "Run" },
            new StartupEntry { Hive = RegistryHive.LocalMachine, RunView = RegistryView.Registry64, Approved = ApprovedKey + "Run" },
            new StartupEntry { Hive = RegistryHive.LocalMachine, RunView = RegistryView.Registry32, Approved = ApprovedKey + "Run32" },
        };
        // On 32-bit Windows both views are the same key.
        if (!Environment.Is64BitOperatingSystem) places = places.Take(2).ToArray();
        var found = new List<StartupEntry>();
        if (StartupPrograms.Length == 0) return found;
        foreach (var place in places)
            try
            {
                using (var root = RegistryKey.OpenBaseKey(place.Hive, place.RunView))
                using (var run = root.OpenSubKey(RunKey))
                {
                    if (run == null) continue;
                    foreach (var name in run.GetValueNames())
                        if (StartupPrograms.Any(program => Mentions(run.GetValue(name) as string, program)))
                            found.Add(new StartupEntry
                            {
                                Hive = place.Hive, RunView = place.RunView, Approved = place.Approved, Name = name
                            });
                }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        return found;
    }

    // Task Manager's value for an entry: 02 on, 03 off followed by when it was turned off (FILETIME at byte 4), 12 bytes.
    public static byte[] ApprovedValue(bool enabled, DateTime when)
    {
        var value = new byte[12];
        value[0] = (byte)(enabled ? 2 : 3);
        if (!enabled) BitConverter.GetBytes(when.ToFileTimeUtc()).CopyTo(value, 4);
        return value;
    }

    static void SetEnabled(StartupEntry entry, bool enabled)
    {
        using (var root = RegistryKey.OpenBaseKey(entry.Hive, entry.Machine ? RegistryView.Registry64 : RegistryView.Default))
        using (var key = root.CreateSubKey(entry.Approved))
            key.SetValue(entry.Name, ApprovedValue(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
    }

    void EndProcesses()
    {
        foreach (var process in Process.GetProcesses())
            using (process)
                try { if (Ours(process.ProcessName)) process.Kill(); }
                catch (Win32Exception) { }
                catch (InvalidOperationException) { }
    }

    public static List<LogitechSoftware> ByIds(IEnumerable<string> ids) { return All.Where(item => ids.Contains(item.Id)).ToList(); }

    // From the screen, on a worker thread. Null, or what went wrong.
    public static string Change(List<LogitechSoftware> software, bool stop)
    {
        string problem = null;
        bool elevate = software.Any(item => item.Services.Any(name => ServiceStatus(name) != null)
            || item.StartupEntries().Any(entry => entry.Machine));
        if (elevate)
        {
            var info = new ProcessStartInfo(Application.ExecutablePath,
                "--services " + (stop ? "stop " : "start ") + string.Join(" ", software.Select(item => item.Id)))
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
            };
            try
            {
                using (var process = Process.Start(info))
                {
                    if (process == null) return "Windows didn't start it";
                    if (!process.WaitForExit(90000)) problem = "Windows didn't finish in time";
                    else if (process.ExitCode != 0)
                    {
                        problem = stop ? "Not all stopped" : "Not all started";
                        // The elevated G-Lite says which, as bits of All's order (RunElevated).
                        var failed = All.Where((item, index) => (process.ExitCode >> index & 1) != 0).Select(item => item.Name).ToList();
                        Log.Add("", "error", "Couldn't " + (stop ? "stop " : "start ") + "all of "
                            + (failed.Count > 0 ? string.Join(", ", failed) : "it") + " (exit code " + process.ExitCode + ")");
                    }
                }
            }
            // 1223: the Windows prompt was cancelled.
            catch (Win32Exception ex) { return ex.NativeErrorCode == 1223 ? "Cancelled in the Windows prompt" : ex.Message; }
        }
        // Done even when the elevated part partly failed, so a service left on doesn't also come with its program at sign-in.
        // The user's own entries, from here: an elevated G-Lite can run as another account, with another HKCU.
        foreach (var item in software)
        {
            foreach (var entry in item.StartupEntries().Where(entry => !entry.Machine))
                try { SetEnabled(entry, !stop); }
                catch (Exception ex)
                {
                    Log.Add("", "error", "Couldn't turn " + (stop ? "off" : "on") + " the startup entry " + entry.Name + ": " + ex.Message);
                }
            if (stop) item.EndProcesses();
        }
        return problem;
    }

    // GLite.exe --services stop|start <id>..., run elevated by Change. The ids pick from All, so it can't touch other services. It
    // returns a bit for each of All that failed, and doesn't log: it doesn't touch Settings or Log, whose files it could find
    // elsewhere (an administrator can write next to the exe where the user's G-Lite couldn't), and Change logs the bits.
    public static int RunElevated(string[] args)
    {
        bool stop = args[1] == "stop";
        if (!stop && args[1] != "start") return 1 << All.Length;
        int failed = 0;
        foreach (var item in ByIds(args.Skip(2)))
        {
            int bit = 1 << Array.IndexOf(All, item);
            foreach (var name in item.Services)
                try { ChangeService(name, stop); }
                catch (Exception) { failed |= bit; }
            foreach (var entry in item.StartupEntries().Where(entry => entry.Machine))
                try { SetEnabled(entry, !stop); }
                catch (Exception) { failed |= bit; }
            if (stop) item.EndProcesses();
        }
        return failed;
    }

    static void ChangeService(string name, bool stop)
    {
        if (ServiceStatus(name) == null) return;
        // ServiceController can't set the start type: sc.exe does, through ChangeServiceConfig.
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"),
            "config \"" + name + "\" start= " + (stop ? "demand" : "auto")) { UseShellExecute = false, CreateNoWindow = true };
        using (var process = Process.Start(info))
            if (!process.WaitForExit(15000) || process.ExitCode != 0) throw new InvalidOperationException("sc config failed");
        using (var service = new ServiceController(name))
        {
            var wanted = stop ? ServiceControllerStatus.Stopped : ServiceControllerStatus.Running;
            if (service.Status == wanted) return;
            if (stop) service.Stop(); else service.Start();
            service.WaitForStatus(wanted, TimeSpan.FromSeconds(20));
        }
    }
}

// The Settings page's Services box: a row for each of LogitechSoftware.All, with its own Stop or Start, and one for Windows Dynamic
// Lighting, read every 3 s while shown.
class ServicesBox : RoundedBox
{
    public const int DesignHeight = RowHeight * Rows;
    const int RowHeight = Ui.RowHeight, BoxWidth = 616, ButtonX = BoxWidth - 14 - 96, StatusX = ButtonX - 12 - 84, Rows = 5;
    readonly bool demo;
    readonly Timer timer = new Timer { Interval = 3000 };
    readonly Label[] detailLabels = new Label[Rows], stateLabels = new Label[Rows];
    readonly FlatButton[] buttons = new FlatButton[Rows];
    readonly string[] problems = new string[Rows];
    readonly SoftwareState[] demoStates =
        { SoftwareState.Running, SoftwareState.NotInstalled, SoftwareState.NotInstalled, SoftwareState.Running };
    SoftwareState[] shown;
    bool lightingOn, busy, reading;
    // A software's id (LogitechSoftware.Id) once Stop worked, on the UI thread.
    public event Action<string> Stopped;
    // Bumped by each Stop or Start, so a read started before it is dropped.
    int sequence;
    DateTime problemsUntil;

    public ServicesBox(bool demo)
    {
        this.demo = demo;
        for (int row = 0; row < Rows; row++)
        {
            int y = RowHeight * row, picked = row;
            Ui.Text(this, row < Rows - 1 ? LogitechSoftware.All[row].Name : "Windows Dynamic Lighting", 14, y + 12, () => Theme.Text,
                9.75f, Ui.Semibold);
            detailLabels[row] = Ui.Text(this, "", 14, y + 33, () => DetailColor(picked), 9f);
            stateLabels[row] = Ui.Text(this, "", StatusX + 13, y, () => Theme.Text, 9f);
            stateLabels[row].Top = y + (RowHeight - stateLabels[row].Height) / 2;
            buttons[row] = Ui.Add(this,
                new FlatButton { Text = row < Rows - 1 ? "Stop" : "Settings", Icon = row < Rows - 1 ? "shield" : null, Visible = false },
                ButtonX, y + 16, 96, 32);
            buttons[row].Click += (s, e) => Clicked(picked);
        }
        timer.Tick += (s, e) => Read();
        VisibleChanged += (s, e) =>
        {
            if (Visible) { Read(); timer.Start(); }
            else timer.Stop();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }

    Color DetailColor(int row)
    {
        return problems[row] != null ? Theme.Error : row == Rows - 1 && lightingOn ? Theme.Warn : Theme.Dim;
    }

    void Read()
    {
        var form = FindForm();
        if (reading || busy || !Visible || (form != null && form.WindowState == FormWindowState.Minimized)) return;
        if (demo) { Show(demoStates.ToArray(), false); return; }
        reading = true;
        int startedAt = sequence;
        Task.Run(() =>
        {
            SoftwareState[] states = null;
            bool lighting = false;
            try
            {
                var running = LogitechSoftware.RunningProcesses();
                states = LogitechSoftware.All.Select(item => item.State(running)).ToArray();
                lighting = KeyboardPage.DynamicLightingOn();
            }
            catch (Exception ex) { Log.Add("", "error", "Couldn't read the services: " + ex.Message); }
            if (!IsHandleCreated || IsDisposed) { reading = false; return; }
            BeginInvoke((Action)(() =>
            {
                reading = false;
                if (states == null || busy) return;
                if (startedAt != sequence) { Read(); return; }
                Show(states, lighting);
            }));
        });
    }

    void Show(SoftwareState[] states, bool lighting)
    {
        shown = states;
        lightingOn = lighting;
        if (DateTime.UtcNow > problemsUntil) for (int row = 0; row < Rows; row++) problems[row] = null;
        for (int row = 0; row < Rows - 1; row++)
        {
            var state = states[row];
            stateLabels[row].Text = state == SoftwareState.Running ? "Running" : state == SoftwareState.Stopped ? "Stopped" : "Not installed";
            buttons[row].Text = state == SoftwareState.Running ? "Stop" : "Start";
            buttons[row].Visible = state != SoftwareState.NotInstalled;
            detailLabels[row].Text = problems[row] ?? LogitechSoftware.All[row].Detail;
        }
        stateLabels[Rows - 1].Text = lighting ? "On" : "Off";
        detailLabels[Rows - 1].Text = lighting ? "May override G-Lite's lighting" : "Windows' own control of RGB devices";
        buttons[Rows - 1].Visible = true;
        foreach (var button in buttons) { button.Enabled = !busy; button.Invalidate(); }
        for (int row = 0; row < Rows; row++) detailLabels[row].ForeColor = DetailColor(row);
        Invalidate();
    }

    void Clicked(int row)
    {
        if (row == Rows - 1) { Process.Start("ms-settings:personalization-lighting"); return; }
        if (shown != null) Change(new[] { row }, shown[row] == SoftwareState.Running);
    }

    void Change(int[] rows, bool stop)
    {
        var software = rows.Select(row => LogitechSoftware.All[row]).ToList();
        string names = string.Join(", ", software.Select(item => item.Name));
        Log.Add("", "you", (stop ? "Stop " : "Start ") + names);
        busy = true;
        sequence++;
        foreach (var row in rows) { stateLabels[row].Text = stop ? "Stopping…" : "Starting…"; problems[row] = null; }
        foreach (var button in buttons) button.Enabled = false;
        if (demo)
        {
            foreach (var row in rows) demoStates[row] = stop ? SoftwareState.Stopped : SoftwareState.Running;
            busy = false;
            if (stop && Stopped != null) foreach (var item in software) Stopped(item.Id);
            Read();
            return;
        }
        Task.Run(() =>
        {
            string problem;
            try { problem = LogitechSoftware.Change(software, stop); }
            catch (Exception ex) { problem = ex.Message; }
            if (problem != null) Log.Add("", "error", (stop ? "Stop " : "Start ") + names + ": " + problem);
            else Log.Add("", "services", (stop ? "Stopped " : "Started ") + names);
            if (!IsHandleCreated || IsDisposed) { busy = false; return; }
            BeginInvoke((Action)(() =>
            {
                busy = false;
                if (problem != null)
                {
                    foreach (var row in rows) problems[row] = problem;
                    problemsUntil = DateTime.UtcNow.AddSeconds(15);
                }
                else if (stop && Stopped != null) foreach (var item in software) Stopped(item.Id);
                Read();
            }));
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        float scale = Width / (float)BoxWidth;
        using (var pen = new Pen(Theme.Border, Math.Max(1, scale)))
            for (int row = 1; row < Rows; row++)
                graphics.DrawLine(pen, 14 * scale, row * RowHeight * scale, Width - 14 * scale, row * RowHeight * scale);
        if (shown == null) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // Pixel edges on whole coordinates, like the text's; GDI+'s default puts the dot half a pixel lower.
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        for (int row = 0; row < Rows; row++)
        {
            bool warn = row < Rows - 1 ? shown[row] == SoftwareState.Running : lightingOn;
            // Half a pixel above the label's middle, level with Segoe UI's lowercase letters.
            float middle = stateLabels[row].Top + stateLabels[row].Height / 2f - 0.5f * scale;
            using (var brush = new SolidBrush(warn ? Theme.Warn : Theme.Accent))
                graphics.FillEllipse(brush, stateLabels[row].Left - 12 * scale, middle - 3.5f * scale, 7 * scale, 7 * scale);
        }
    }
}
