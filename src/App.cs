using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TinyTodo
{
    internal static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")]
        internal static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam);
        internal static readonly uint ShowMessage = RegisterWindowMessage("TinyTodo.Show.2026.v1");
        internal static readonly uint ExitMessage = RegisterWindowMessage("TinyTodo.Exit.2026.v2");

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 1 && args[0] == "--cleanup-data") { Environment.ExitCode = Uninstaller.CleanupData(); return; }
            if (args.Length == 2 && args[0] == "--uninstall") { Environment.ExitCode = Uninstaller.Run(args[1]); return; }
            bool first;
            using (var mutex = new Mutex(true, @"Local\TinyTodo.2026.v1", out first))
            {
                if (!first) { PostMessage(new IntPtr(0xffff), ShowMessage, IntPtr.Zero, IntPtr.Zero); return; }
                try
                {
                    var locations = DataLocations.Default(); locations.Load();
                    string path = locations.TaskPath;
                    var store = new Store(path);
                    try { store.Load(); }
                    catch (Exception ex)
                    {
                        if (!File.Exists(path + ".bak") || ThemedDialog.Show("任务文件无法读取：\n" + ex.Message +
                            "\n\n是否恢复上一次自动备份？当前文件会另存保留。\n选择“否”将退出。", "TinyTodo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        {
                            ThemedDialog.Show("未修改任务文件。请查看：\n" + path, "TinyTodo"); return;
                        }
                        store.RecoverBackup();
                    }
                    // Every launch starts with the floating entry and task window available.
                    if (!store.Current.Window.Floating) store.Change(s => s.Window.Floating = true);
                    Application.Run(new MainForm(store, locations));
                }
                catch (Exception ex) { ThemedDialog.Show(ex.Message, "TinyTodo 无法启动", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }

    // This code-built UI uses ONE scaling strategy: system DPI, explicit pixels.
    // AutoScaleMode.None prevents fixed row heights being scaled a second time.
    internal static class Ui
    {
        internal static readonly float Scale = GetScale();
        internal static readonly Color Ink = Theme.Ink;
        internal static readonly Color Green = Theme.Rose;
        internal static void DrawImportant(Graphics graphics, RectangleF bounds)
        {
            var state = graphics.Save(); graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float x = bounds.Left + bounds.Width * .48F;
            Color red = Color.FromArgb(210, 66, 90);
            using (var pen = new Pen(red, Math.Max(1.4F, bounds.Width * .29F)))
            {
                pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                graphics.DrawLine(pen, x + bounds.Width * .04F, bounds.Top + bounds.Height * .18F, x, bounds.Top + bounds.Height * .58F);
            }
            float dot = bounds.Width * .32F;
            using (var brush = new SolidBrush(red)) graphics.FillEllipse(brush, x - dot / 2, bounds.Top + bounds.Height * .77F, dot, dot);
            graphics.Restore(state);
        }
        internal static Bitmap ImportantImage()
        {
            var bitmap = new Bitmap(U(7), U(13));
            using (var g = Graphics.FromImage(bitmap)) DrawImportant(g, new RectangleF(0, 0, bitmap.Width, bitmap.Height));
            return bitmap;
        }
        private static float GetScale() { using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96F; }
        [DllImport("user32.dll")] private static extern short GetKeyState(int key);
        internal static bool WindowsKeyDown { get { return (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0; } }
        internal static bool ExactModifiers(KeyEventArgs e, Keys modifiers)
        { return e.Modifiers == modifiers && !WindowsKeyDown; }
        internal static int ViewTabGap { get { return U(3); } }
        internal static int U(int value) { return (int)Math.Round(value * Scale); }
        internal static void Setup(Form f, string title, int width, int height)
        {
            f.AutoScaleMode = AutoScaleMode.None;
            f.Font = Theme.Font(9.5F, FontStyle.Regular, "任务列表");
            f.Text = title; f.ForeColor = Ink; f.BackColor = Theme.Canvas;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(U(width), U(height));
            f.MinimumSize = new Size(U(380), U(300));
            f.MaximizeBox = false;
        }
        internal static Button Button(string title, EventHandler click)
        {
            var b = new SoftButton { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = Theme.Font(9.5F, FontStyle.Regular, title),
                Padding = new Padding(U(9), U(5), U(9), U(5)), Margin = new Padding(U(3)),
                FlatStyle = FlatStyle.Flat, BackColor = Color.White, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Theme.Border;
            if (title == "新增" || title == "保存") { b.BackColor = title == "新增" ? Theme.AddAction : Theme.RoseSoft; b.ForeColor = Theme.Ink; }
            b.Click += click; return b;
        }
        internal static FlowLayoutPanel Bar()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty };
        }
        internal static Label Label(string text)
        {
            return new Label { Text = text, AutoSize = true, Dock = DockStyle.Fill, Font = Theme.Font(9.5F, FontStyle.Regular, text),
                Margin = new Padding(U(3), U(7), U(3), U(7)) };
        }
        internal static TableLayoutPanel Root(params RowStyle[] rows)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows.Length,
                Padding = new Padding(U(12)) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var row in rows) root.RowStyles.Add(row);
            return root;
        }
        internal static RowStyle Auto() { return new RowStyle(SizeType.AutoSize); }
        internal static RowStyle Fill() { return new RowStyle(SizeType.Percent, 100); }
        internal static string Status(State s, Todo t)
        {
            if (t.Done) return "已完成";
            int count = Rules.Blockers(s, t).Count;
            if (count > 0) return "等待 " + count + " 项";
            return t.Due != null && String.CompareOrdinal(t.Due, DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) < 0 ? "已逾期" : "可开始";
        }
        internal static void Fit(Form f)
        {
            Rectangle area = Screen.FromRectangle(f.Bounds).WorkingArea;
            f.Size = new Size(Math.Min(f.Width, area.Width), Math.Min(f.Height, area.Height));
            f.Location = new Point(Math.Max(area.Left, Math.Min(f.Left, area.Right - f.Width)),
                Math.Max(area.Top, Math.Min(f.Top, area.Bottom - f.Height)));
        }
        internal static void ScrollRoot(Form form, TableLayoutPanel root, int minimumHeight)
        {
            form.Controls.Add(new ScrollSurface(root, U(minimumHeight)));
        }
    }

    // Read foreground state only: no global input hooks, key interception or focus forcing.
    internal static class DesktopActivity
    {
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect bounds);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int capacity);
        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
        private static readonly uint ProcessId = (uint)Process.GetCurrentProcess().Id;
        internal static bool OwnsForeground
        {
            get { uint id; GetWindowThreadProcessId(GetForegroundWindow(), out id); return id == ProcessId; }
        }
        internal static bool CoversScreen(Rectangle window, Rectangle screen)
        { return window.Left <= screen.Left && window.Top <= screen.Top && window.Right >= screen.Right && window.Bottom >= screen.Bottom; }
        internal static bool ShouldYield
        {
            get
            {
                if (OwnsForeground) return false;
                int state;
                if (SHQueryUserNotificationState(out state) == 0 && state >= 1 && state <= 4) return true;
                IntPtr foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero) return false;
                var name = new System.Text.StringBuilder(256); GetClassName(foreground, name, name.Capacity);
                if (name.ToString() == "Progman" || name.ToString() == "WorkerW" || name.ToString() == "Shell_TrayWnd") return false;
                NativeRect r;
                return GetWindowRect(foreground, out r) && CoversScreen(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), Screen.FromHandle(foreground).Bounds);
            }
        }
    }

    internal sealed class MainForm : AppWindow
    {
        private readonly Store store;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly FloatingIcon bubble;
        private readonly TaskTable grid;
        private readonly TaskViews views;
        private readonly Button currentButton, historyButton;
        private readonly FloatingSwitch floatingButton;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Icon appIcon, trayIcon;
        private readonly Label clock;
        private readonly ZoneSelector clockZone;
        private bool settingClock;
        private sealed class ZoneChoice
        {
            internal string Id, Name;
            public override string ToString() { return Name; }
        }
        private bool history, exiting, completionQueued, trayMenuQueued, desktopMenuOpen;
        private DateTime today = DateTime.Today;
        public MainForm(Store store, DataLocations locations)
        {
            this.store = store;
            Ui.Setup(this, "TinyTodo 3.5.2", 720, 500);
            StartPosition = FormStartPosition.Manual;
            appIcon = MakeIcon(); Icon = appIcon;
            var root = Ui.Root(Ui.Auto(), Ui.Fill(), Ui.Auto());
            var top = new MainToolbar { Margin = new Padding(3, 3, 3, 0) };
            currentButton = Ui.Button("待办", delegate { SwitchHistory(false); });
            historyButton = Ui.Button("历史", delegate { SwitchHistory(true); });
            foreach (SoftButton tab in new Button[] { currentButton, historyButton }) { tab.IndexTab = true; tab.Margin = new Padding(Ui.U(1), Ui.U(3), Ui.U(1), 0); }
            floatingButton = AddFloatingSwitch(value => { if (value != store.Current.Window.Floating) ToggleFloating(); });
            AddCaptionAction(Glyph.Settings, "设置", delegate
            { using (var settings = new SettingsForm(store, locations)) { settings.TopMost = TopMost; settings.ShowDialog(this); } });
            var addButton = new AddTaskButton(); addButton.Click += delegate { Add(); };
            top.LeftItems.AddRange(new Control[] { currentButton, historyButton, addButton });
            views = new TaskViews(false) { Margin = new Padding(3, 0, 3, 3) }; grid = views.First; top.SetNavigation(views.DetachNavigation());
            views.BeforeTree = delegate { views.SetShowDone(history); };
            grid.SortDate = delegate { Change(s => s.Window.DateDescending = !s.Window.DateDescending); };
            grid.ToggleDateMode = delegate { Change(s => s.Window.DateCountdown = !s.Window.DateCountdown); };
            MinimumSize = SizeFromClientSize(new Size(grid.RequiredWidth + Ui.U(64), Ui.U(280)));
            grid.OpenTask = QueuePreview; grid.ToggleTask = QueueComplete;
            grid.TaskMenu = delegate(string id, Point point) { ShowTaskMenu(grid, id, point); };
            views.Graph.OpenTask = QueuePreview; views.Graph.ToggleTask = QueueComplete;
            views.Graph.TaskMenu = delegate(string id, Point point) { ShowTaskMenu(views.Graph, id, point); };
            var bottom = Ui.Bar();
            clock = Ui.Label(""); clock.ForeColor = Theme.Muted; clock.Font = Theme.Font(9F, FontStyle.Regular, "2026 UTC"); clock.AccessibleName = "当前日期和时间";
            clockZone = new ZoneSelector { Margin = new Padding(Ui.U(3)), AccessibleName = "时区" };
            clockZone.Items.Add(new ZoneChoice { Id = null, Name = "系统时区" });
            clockZone.Items.Add(new ZoneChoice { Id = WorldClock.AoE, Name = "全球时间 · AoE (UTC−12)" });
            clockZone.Items.Add(new ZoneChoice { Id = WorldClock.UK, Name = "英国 · London" });
            clockZone.Items.Add(new ZoneChoice { Id = WorldClock.China, Name = "中国 · Beijing" });
            foreach (TimeZoneInfo zone in TimeZoneInfo.GetSystemTimeZones())
                if (zone.Id != WorldClock.UK && zone.Id != WorldClock.China)
                    clockZone.Items.Add(new ZoneChoice { Id = zone.Id, Name = zone.DisplayName });
            clockZone.SelectionChangeCommitted += delegate
            {
                if (settingClock) return;
                var choice = (ZoneChoice)clockZone.SelectedItem;
                try { WorldClock.Zone(choice.Id); Change(s => s.Window.ClockZoneId = choice.Id); }
                catch (Exception ex) { Error(ex); }
                SelectClock(); UpdateClock();
            };
            bottom.Controls.Add(clock); bottom.Controls.Add(clockZone);
            root.Controls.Add(top, 0, 0); root.Controls.Add(views, 0, 1); root.Controls.Add(bottom, 0, 2); Controls.Add(root);
            menu = new SoftMenu { Renderer = new TaskMenuRenderer(), ShowImageMargin = false, BackColor = Theme.Canvas, ForeColor = Theme.Ink, Font = Theme.Font(9.5F, FontStyle.Regular, "任务列表") };
            menu.Items.Add("展开任务表", null, delegate { ShowMain(); });
            menu.Items.Add("添加任务", null, delegate { ShowMain(); Add(); });
            menu.Items.Add("开启／关闭悬浮图标", null, delegate { ToggleFloating(); });
            menu.Items.Add("完成历史", null, delegate { SwitchHistory(true); ShowMain(false); });
            menu.Items.Add("打开数据文件夹", null, delegate
            {
                try { string dir = Path.GetDirectoryName(store.PathName); Directory.CreateDirectory(dir); Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); }
                catch (Exception ex) { Error(ex); }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { exiting = true; Close(); });
            menu.Opening += delegate { desktopMenuOpen = true; };
            menu.Closed += delegate { desktopMenuOpen = false; };
            // The tray release must finish before the popup takes foreground ownership.
            trayIcon = MakeTrayIcon();
            tray = new NotifyIcon { Icon = trayIcon, Text = "TinyTodo 3.5.2", Visible = true };
            tray.MouseUp += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Right) QueueTrayMenu(Cursor.Position); };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowMain(); };
            bubble = new FloatingIcon(ToggleFromBubble, SaveIconPosition, menu);
            var w = store.Current.Window;
            Location = w.HasPosition ? new Point(w.X, w.Y) : new Point(Screen.PrimaryScreen.WorkingArea.Right - Width - Ui.U(24), Screen.PrimaryScreen.WorkingArea.Top + Ui.U(40));
            bubble.Location = w.HasIconPosition ? new Point(w.IconX, w.IconY) : new Point(Screen.PrimaryScreen.WorkingArea.Right - bubble.Width - Ui.U(16), Screen.PrimaryScreen.WorkingArea.Top + Ui.U(100));
            Ui.Fit(this); Ui.Fit(bubble);
            ApplyFloating();
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Collapse(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            { SavePosition(); if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (Ui.ExactModifiers(e, Keys.Control) && e.KeyCode == Keys.N) { e.SuppressKeyPress = true; Add(); }
                if (Ui.ExactModifiers(e, Keys.None) && e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Collapse(); }
            };
            timer = new System.Windows.Forms.Timer { Interval = 250 };
            timer.Tick += delegate { UpdateDesktopVisibility(DesktopActivity.ShouldYield); UpdateClock(); if (today != DateTime.Today) { today = DateTime.Today; Render(); } };
            Microsoft.Win32.SystemEvents.TimeChanged += SystemTimeChanged;
            timer.Start(); SelectClock(); UpdateClock(); Render();
        }
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        private void QueueTrayMenu(Point point)
        {
            if (trayMenuQueued || IsDisposed || Disposing) return;
            trayMenuQueued = true;
            BeginInvoke(new Action(delegate
            {
                trayMenuQueued = false;
                if (IsDisposed || Disposing || menu.IsDisposed) return;
                menu.Show(point);
                // Only the explicit right-click activates the menu, never the task window.
                if (menu.Visible) SetForegroundWindow(menu.Handle);
            }));
        }
        private void SwitchHistory(bool showHistory)
        { views.ClearCompletionFeedback(); history = showHistory; views.SelectView(0); views.SetShowDone(history); Render(); }
        private void SelectClock()
        {
            settingClock = true;
            try
            {
                var choice = clockZone.Items.Cast<ZoneChoice>().FirstOrDefault(z => z.Id == store.Current.Window.ClockZoneId);
                if (choice == null)
                {
                    choice = new ZoneChoice { Id = store.Current.Window.ClockZoneId, Name = "时区不可用 · " + store.Current.Window.ClockZoneId };
                    clockZone.Items.Add(choice);
                }
                clockZone.SelectedItem = choice;
            }
            finally { settingClock = false; }
        }
        private void UpdateClock()
        {
            try { clock.Text = WorldClock.Display(WorldClock.At(DateTimeOffset.UtcNow, store.Current.Window.ClockZoneId)); }
            catch (TimeZoneNotFoundException) { clock.Text = "时区不可用"; }
            catch (InvalidTimeZoneException) { clock.Text = "时区规则不可用"; }
        }
        private void SystemTimeChanged(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(new Action(delegate { if (!IsDisposed && !Disposing) { TimeZoneInfo.ClearCachedData(); UpdateClock(); Render(); } })); }
            catch (InvalidOperationException) { }
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        private static Icon MakeTrayIcon()
        {
            // Share the complete EXE artwork, selecting the system tray's pixel size.
            return MakeIcon(Math.Max(16, GetSystemMetrics(49)));
        }
        private static Icon MakeIcon() { return MakeIcon(Ui.U(32)); }
        private static Icon MakeIcon(int size)
        {
            if (File.Exists(Theme.Asset("app.ico")))
                using (var icon = new Icon(Theme.Asset("app.ico"), size, size)) return (Icon)icon.Clone();
            using (var b = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(b)) using (var brush = new SolidBrush(Ui.Green)) using (var p = new Pen(Color.White, 3))
                { g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(Color.Transparent); g.FillEllipse(brush, 1, 1, 30, 30); g.DrawLines(p, new Point[] { new Point(8, 16), new Point(14, 22), new Point(25, 10) }); }
                IntPtr handle = b.GetHicon();
                try { using (var borrowed = System.Drawing.Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)Program.ShowMessage) ShowMain();
            if (m.Msg == (int)Program.ExitMessage) { exiting = true; Close(); return; }
            base.WndProc(ref m);
        }
        private Form ActiveDialog()
        {
            Form target = this;
            while (true)
            {
                Form child = target.OwnedForms.LastOrDefault(f => f.Visible && f.Modal && !f.IsDisposed);
                if (child == null) return target;
                target = child;
            }
        }
        internal void ToggleFromBubble()
        {
            // Visible also includes obscured windows and owners disabled by modal dialogs.
            if (Visible && Enabled && WindowState == FormWindowState.Normal && DesktopActivity.GetForegroundWindow() == Handle) Collapse();
            else ShowMain();
        }
        private void ShowMain(bool resetView = true)
        {
            Form dialog = ActiveDialog();
            if (dialog != this)
            {
                if (dialog.WindowState == FormWindowState.Minimized) dialog.WindowState = FormWindowState.Normal;
                Ui.Fit(dialog); dialog.Show(); dialog.BringToFront(); dialog.Activate(); return;
            }
            if (resetView) { SwitchHistory(false); views.SelectView(0); }
            WindowState = FormWindowState.Normal;
            if (store.Current.Window.Floating && !Visible)
            {
                Rectangle area = Screen.FromControl(bubble).WorkingArea;
                Location = new Point(bubble.Left - Width - Ui.U(8), bubble.Top);
                if (Left < area.Left) Left = bubble.Right + Ui.U(8);
            }
            Show(); WindowState = FormWindowState.Normal; Ui.Fit(this); BringToFront(); Activate();
        }
        private void Collapse() { SavePosition(); Hide(); }
        private void ApplyFloating()
        {
            bool floating = store.Current.Window.Floating;
            ShowInTaskbar = !floating;
            floatingButton.IsFloating = floating; floatingButton.Invalidate();
            UpdateDesktopVisibility(DesktopActivity.ShouldYield);
        }
        // Polling never activates a window. Restoring the bubble also uses WS_EX_NOACTIVATE.
        internal void UpdateDesktopVisibility(bool yieldToFullscreen)
        {
            // Do not change floating-icon visibility under an active menu.
            // Task windows use normal Windows stacking; polling never repins them.
            if (desktopMenuOpen)
            {
                if (!yieldToFullscreen) return;
                menu.Close(ToolStripDropDownCloseReason.AppFocusChange);
            }
            bool floating = store.Current.Window.Floating;
            bool showBubble = floating && !yieldToFullscreen;
            if (bubble.Visible != showBubble) { if (showBubble) bubble.Show(); else bubble.Hide(); }
        }
        protected override bool ShowWithoutActivation
        { get { return store == null || store.Current.Window.Floating || DesktopActivity.ShouldYield; } }
        private void ToggleFloating()
        {
            bool wasVisible = Visible; Point location = Location;
            if (!Change(s => s.Window.Floating = !s.Window.Floating)) return;
            ApplyFloating();
            if (wasVisible) { Location = location; Show(); Activate(); }
            else if (!store.Current.Window.Floating) ShowMain();
        }
        private void SaveIconPosition(Point point)
        {
            try { store.Change(s => { s.Window.IconX = point.X; s.Window.IconY = point.Y; s.Window.HasIconPosition = true; }); }
            catch (Exception ex) { Error(ex); }
        }
        private void SavePosition()
        {
            if (WindowState != FormWindowState.Normal) return;
            try { store.Change(s => { s.Window.X = Left; s.Window.Y = Top; s.Window.HasPosition = true; }); }
            catch (Exception ex) { Error(ex); }
        }
        private void QueueComplete(string id)
        {
            if (id == null || completionQueued || views.HasCompletionFeedback(id)) return;
            completionQueued = true;
            BeginInvoke(new Action(delegate
            {
                try
                {
                    store.Change(s => { if (Rules.Get(s, id).Done) Rules.Restore(s, id); else Rules.Complete(s, id); });
                    views.ShowCompletionFeedback(store.Current, id); Render();
                }
                catch (Exception ex) { Error(ex); Render(); }
                finally { completionQueued = false; }
            }));
        }
        private bool previewQueued;
        private void QueuePreview(string id)
        {
            if (id == null || previewQueued) return;
            previewQueued = true;
            BeginInvoke(new Action(delegate
            {
                try { using (var preview = new TaskPreview(store, id)) { preview.TopMost = TopMost; preview.ShowDialog(this); } Render(); }
                finally { previewQueued = false; }
            }));
        }
        private void Add()
        {
            using (var editor = new TaskEditor(store, null)) { editor.TopMost = TopMost; if (editor.ShowDialog(this) == DialogResult.OK) Render(); }
        }
        private void ShowTaskMenu(Control source, string id, Point point)
        {
            TaskActions.Menu(this, source, point, store, id, Render);
        }
        private void Render()
        {
            State s = store.Current;
            var items = Rules.ByDate(s.Tasks.Where(t => t.Done == history), s.Window.DateDescending).ToList();
            views.Render(s, null, items);
            currentButton.Text = "待办 " + s.Tasks.Count(t => !t.Done); historyButton.Text = "历史 " + s.Tasks.Count(t => t.Done);
            ((SoftButton)currentButton).SelectedTab = !history; currentButton.Invalidate();
            ((SoftButton)historyButton).SelectedTab = history; historyButton.Invalidate();
            Text = history ? "TinyTodo 3.5 · 历史" : "TinyTodo 3.5.2";
        }
        private bool Change(Action<State> action)
        { try { store.Change(action); Render(); return true; } catch (Exception ex) { Error(ex); return false; } }
        private void Error(Exception ex) { ThemedDialog.Show(this, ex.Message, "TinyTodo", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (timer != null) timer.Dispose(); if (bubble != null) bubble.Dispose();
                Microsoft.Win32.SystemEvents.TimeChanged -= SystemTimeChanged;
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (menu != null) menu.Dispose(); if (trayIcon != null) trayIcon.Dispose(); if (appIcon != null) appIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class FloatingIcon : Form
    {
        private readonly Action toggle;
        private readonly Action<Point> save;
        private Point down, origin;
        private bool dragging, moved, toggleQueued;
        private readonly ToolTip tip = new ToolTip();
        private readonly Bitmap[] animationFrames;
        private readonly int[] frameDelays;
        private readonly System.Windows.Forms.Timer animationTimer;
        private int frameIndex;
        internal FloatingIcon(Action toggle, Action<Point> save, ContextMenuStrip menu)
        {
            this.toggle = toggle; this.save = save; Icon = Theme.AppIcon;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; TabStop = false; ImeMode = ImeMode.NoControl; StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(Ui.U(36), Ui.U(36));
            BackColor = Color.Magenta; TransparencyKey = Color.Magenta;
            animationFrames = Theme.LoadFloatingFrames(ClientSize.Width, out frameDelays);
            ClientSize = animationFrames[0].Size;
            animationTimer = new System.Windows.Forms.Timer { Interval = frameDelays[0] };
            animationTimer.Tick += delegate
            {
                frameIndex = (frameIndex + 1) % animationFrames.Length;
                animationTimer.Interval = frameDelays[frameIndex]; Invalidate();
            };
            ContextMenuStrip = menu; Cursor = Cursors.Hand; DoubleBuffered = true;
            tip.SetToolTip(this, "TinyTodo：点击展开／收起，拖动移动，右键菜单");
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; return cp; } }
        protected override void WndProc(ref Message m)
        {
            // Include active-window tracking: hovering/clicking the bubble must not
            // switch the focused input thread or its keyboard layout.
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
            base.WndProc(ref m);
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (animationTimer != null) animationTimer.Enabled = Visible && animationFrames.Length > 1;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (animationFrames != null) e.Graphics.DrawImageUnscaled(animationFrames[frameIndex], 0, 0);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { down = Cursor.Position; origin = Location; dragging = true; moved = false; Capture = true; } }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (!dragging) return;
            Point p = Cursor.Position; int dx = p.X - down.X, dy = p.Y - down.Y;
            if (Math.Abs(dx) > Ui.U(4) || Math.Abs(dy) > Ui.U(4)) moved = true;
            if (moved) { Location = new Point(origin.X + dx, origin.Y + dy); Ui.Fit(this); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (e.Button != MouseButtons.Left || !dragging) return;
            dragging = false; Capture = false;
            if (moved) save(Location);
            else if (ClientRectangle.Contains(e.Location) && !toggleQueued)
            {
                toggleQueued = true;
                BeginInvoke(new Action(delegate
                {
                    try { if (!IsDisposed) toggle(); }
                    finally { toggleQueued = false; }
                }));
            }
        }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragging = false; }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (animationTimer != null) animationTimer.Dispose();
                if (animationFrames != null) foreach (var frame in animationFrames) frame.Dispose();
                tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
