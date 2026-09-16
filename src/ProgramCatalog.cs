using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TinyTodo
{
    internal sealed class ProgramEntry
    {
        public string Name { get; set; }
        public string Executable { get; set; }
        public string Source { get; set; }
        internal string Key { get { return Path.GetFileNameWithoutExtension(Executable); } }
        public override string ToString() { return Name + " · " + Key + ".exe"; }
    }
    internal static class ProgramCatalog
    {
        internal static ProgramEntry FromPath(string path, string name, string source)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            path = Environment.ExpandEnvironmentVariables(path.Trim());
            if (path.StartsWith("\"")) { int end = path.IndexOf('"', 1); if (end < 0) return null; path = path.Substring(1, end - 1); }
            else { int comma = path.LastIndexOf(','); int index; if (comma > 0 && Int32.TryParse(path.Substring(comma + 1).Trim(), out index)) path = path.Substring(0, comma); }
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
            string key = Path.GetFileNameWithoutExtension(path);
            if (key.StartsWith("unins", StringComparison.OrdinalIgnoreCase) || key.Equals("msiexec", StringComparison.OrdinalIgnoreCase)) return null;
            if (String.IsNullOrWhiteSpace(name))
                try { name = FileVersionInfo.GetVersionInfo(path).FileDescription; } catch (Exception) { }
            return new ProgramEntry { Name = String.IsNullOrWhiteSpace(name) ? key : name, Executable = path, Source = source };
        }
        internal static List<ProgramEntry> Scan(Action<List<ProgramEntry>> publish, Func<bool> cancelled, Action<string> warning)
        {
            var entries = new Dictionary<string, ProgramEntry>(StringComparer.OrdinalIgnoreCase);
            Action<ProgramEntry> add = e => { if (e != null && !entries.ContainsKey(e.Key)) entries.Add(e.Key, e); };
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    if (cancelled()) return entries.Values.ToList();
                    try
                    {
                        using (var root = RegistryKey.OpenBaseKey(hive, view))
                        {
                            using (var paths = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
                                if (paths != null) foreach (string key in paths.GetSubKeyNames())
                                    try { using (var item = paths.OpenSubKey(key)) if (item != null) add(FromPath(item.GetValue("") as string, null, "已安装")); } catch (Exception) { }
                            using (var apps = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                                if (apps != null) foreach (string key in apps.GetSubKeyNames())
                                    try { using (var item = apps.OpenSubKey(key)) if (item != null) add(FromPath(item.GetValue("DisplayIcon") as string, item.GetValue("DisplayName") as string, "已安装")); } catch (Exception) { }
                        }
                    }
                    catch (Exception) { }
                }
            object shell = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                foreach (string directory in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
                    foreach (string shortcut in Shortcuts(directory, cancelled))
                    {
                        if (cancelled()) return entries.Values.ToList();
                        object link = null;
                        try
                        {
                            link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcut });
                            string path = link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null) as string;
                            var entry = FromPath(path, Path.GetFileNameWithoutExtension(shortcut), "开始菜单");
                            if (entry != null) entries[entry.Key] = entry;
                        }
                        catch (Exception) { }
                        finally { if (link != null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link); }
                    }
            }
            catch (Exception) { }
            finally { if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
            foreach (Process process in Process.GetProcesses())
                using (process)
                    try
                    {
                        bool hasWindow = process.MainWindowHandle != IntPtr.Zero;
                        ProgramEntry entry = null;
                        try { entry = FromPath(process.MainModule.FileName, null, "正在运行"); } catch (Exception) { }
                        if (!hasWindow && (entry == null || entry.Executable.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
                        if (entry == null) entry = new ProgramEntry { Name = process.ProcessName, Executable = process.ProcessName + ".exe", Source = "正在运行" };
                        if (entries.ContainsKey(entry.Key)) entries[entry.Key].Source += " · 正在运行"; else add(entry);
                    }
                    catch (Exception) { }
            publish(entries.Values.ToList());
            // Read installed packaged-app manifests on demand. No app is launched and no registry/data is changed.
            if (!cancelled())
                try
                {
                    const string script = "$ErrorActionPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.UTF8Encoding]::new(); $titles=@{}; Get-StartApps | ForEach-Object { $titles[$_.AppID]=$_.Name }; $items=@(Get-AppxPackage | ForEach-Object { $p=$_; $m=Get-AppxPackageManifest -Package $p.PackageFullName; foreach($a in $m.Package.Applications.Application) { $exe=[string]$a.Executable; if($exe.EndsWith('.exe') -and $a.VisualElements.AppListEntry -ne 'none') { $n=$titles[$p.PackageFamilyName+'!'+$a.Id]; if(!$n) { $n=[string]$a.VisualElements.DisplayName }; if(!$n -or $n.StartsWith('ms-resource:')) { $n=$p.Name }; [pscustomobject]@{Name=$n;Executable=(Join-Path $p.InstallLocation $exe);Source='Microsoft Store'} } } }); ConvertTo-Json -InputObject $items -Compress";
                    var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"), "-NoLogo -NoProfile -NonInteractive -Command \"" + script + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                    using (var helper = Process.Start(info))
                    {
                        var output = new StringBuilder(); object gate = new object();
                        helper.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock (gate) output.AppendLine(e.Data); };
                        helper.ErrorDataReceived += delegate { };
                        helper.BeginOutputReadLine(); helper.BeginErrorReadLine();
                        var watch = Stopwatch.StartNew();
                        while (!helper.WaitForExit(100))
                            if (cancelled() || watch.ElapsedMilliseconds > 15000) { try { helper.Kill(); } catch (InvalidOperationException) { } if (!cancelled()) warning("Store 应用查找超时"); return entries.Values.ToList(); }
                        helper.WaitForExit(); if (helper.ExitCode != 0) warning("部分 Store 应用未能读取"); string json; lock (gate) json = output.ToString();
                        if (!String.IsNullOrWhiteSpace(json))
                            foreach (var entry in new JavaScriptSerializer().Deserialize<List<ProgramEntry>>(json) ?? new List<ProgramEntry>()) add(FromPath(entry.Executable, entry.Name, entry.Source));
                    }
                }
                catch (Exception) { warning("部分 Store 应用未能读取"); }
            return entries.Values.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        private static IEnumerable<string> Shortcuts(string directory, Func<bool> cancelled)
        {
            if (String.IsNullOrEmpty(directory) || cancelled()) yield break;
            string[] files, directories;
            try { files = Directory.GetFiles(directory, "*.lnk"); directories = Directory.GetDirectories(directory); } catch (Exception) { yield break; }
            foreach (string file in files) yield return file;
            foreach (string child in directories)
            {
                if (cancelled()) yield break;
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (string file in Shortcuts(child, cancelled)) yield return file;
            }
        }
    }

    internal sealed class ProgramChecklist : Control, IWheelTarget
    {
        private List<ProgramEntry> rows = new List<ProgramEntry>();
        private readonly ThinScroll scroll = new ThinScroll();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 30 };
        private readonly Dictionary<string, DateTime> feedback = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private int first, selected = -1, wheelRemainder;
        internal Func<string, bool> IsChecked;
        internal Action<ProgramEntry> Picked;
        internal string EmptyText = "暂无程序";
        internal event EventHandler FeedbackFinished;
        internal int RowHeight { get { return Ui.U(48); } }
        internal ProgramChecklist()
        {
            DoubleBuffered = true; TabStop = true; BackColor = Color.White; Font = Theme.Font(9.5F, FontStyle.Regular, "程序");
            scroll.Dock = DockStyle.Right; Controls.Add(scroll); scroll.Changed = value => { first = value; Invalidate(); };
            timer.Tick += delegate { foreach (string key in feedback.Where(p => (DateTime.UtcNow - p.Value).TotalMilliseconds >= 520).Select(p => p.Key).ToArray()) feedback.Remove(key); Invalidate(); if (feedback.Count == 0) { timer.Stop(); if (FeedbackFinished != null) FeedbackFinished(this, EventArgs.Empty); } };
        }
        internal void SetRows(IEnumerable<ProgramEntry> values) { rows = values.ToList(); selected = Math.Min(selected, rows.Count - 1); Sync(); Invalidate(); }
        internal void Pulse(string key) { feedback[key] = DateTime.UtcNow; timer.Start(); Invalidate(); }
        private int Page { get { return Math.Max(1, Height / RowHeight); } }
        private void Sync() { first = Math.Max(0, Math.Min(first, rows.Count - Page)); scroll.SetRange(Math.Max(0, rows.Count - Page), Page, first); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (scroll != null) Sync(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (rows.Count == 0) TextRenderer.DrawText(e.Graphics, EmptyText, Font, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            for (int i = first; i < Math.Min(rows.Count, first + Page + 1); i++)
            {
                ProgramEntry row = rows[i]; var rect = new Rectangle(Ui.U(3), (i - first) * RowHeight + Ui.U(2), Width - Ui.U(17), RowHeight - Ui.U(4));
                if (i == selected || feedback.ContainsKey(row.Key)) using (var path = Theme.Rounded(rect, Ui.U(7))) using (var brush = new SolidBrush(Theme.RoseSoft)) e.Graphics.FillPath(brush, path);
                Theme.Check(e.Graphics, new RectangleF(rect.Left + Ui.U(7), rect.Top + (rect.Height - Ui.U(14)) / 2F, Ui.U(14), Ui.U(14)), IsChecked != null && IsChecked(row.Key));
                var name = new Rectangle(rect.Left + Ui.U(30), rect.Top, rect.Width - Ui.U(32), rect.Height / 2);
                var detail = new Rectangle(name.Left, name.Bottom, name.Width, rect.Height - name.Height);
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                TextRenderer.DrawText(e.Graphics, row.Name, Font, name, Theme.Ink, flags);
                TextRenderer.DrawText(e.Graphics, row.Key + ".exe" + (String.IsNullOrEmpty(row.Source) ? "" : " · " + row.Source), Font, detail, Theme.Muted, flags);
            }
        }
        internal void Pick(int index) { if (index < 0 || index >= rows.Count) return; selected = index; ProgramEntry row = rows[index]; if (Picked != null) Picked(row); Pulse(row.Key); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) { Focus(); Pick(first + e.Y / RowHeight); } }
        bool IWheelTarget.CanScrollWheel(int delta) { return delta > 0 ? first > 0 : first < scroll.Maximum; }
        void IWheelTarget.ScrollWheel(int delta) { ScrollRows(delta); }
        private void ScrollRows(int delta) { first -= WheelInput.Steps(ref wheelRemainder, delta) * 3; Sync(); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { var handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true; ScrollRows(e.Delta); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!Ui.ExactModifiers(e, Keys.None)) { base.OnKeyDown(e); return; }
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) Pick(selected < 0 ? 0 : selected);
            else if (e.KeyCode == Keys.Up) selected = Math.Max(0, selected - 1);
            else if (e.KeyCode == Keys.Down) selected = Math.Min(rows.Count - 1, selected + 1); else { base.OnKeyDown(e); return; }
            if (selected < first) first = selected; if (selected >= first + Page) first = selected - Page + 1; Sync(); Invalidate(); e.SuppressKeyPress = true;
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class ProgramPicker : AppWindow
    {
        private readonly ProgramChecklist list = new ProgramChecklist { Dock = DockStyle.Fill };
        private readonly TextBox search = new TextBox { Dock = DockStyle.Fill, AccessibleName = "搜索应用名或程序名" };
        private readonly Label status = Ui.Label("正在查找应用…");
        private List<ProgramEntry> entries = new List<ProgramEntry>();
        private volatile bool cancelled;
        private string catalogWarning = "";
        internal ProgramPicker(Func<string, bool> contains, Action<ProgramEntry> toggle)
        {
            Ui.Setup(this, "选择程序", 600, 570);
            var root = Ui.Root(Ui.Auto(), Ui.Auto(), Ui.Fill(), Ui.Auto(), Ui.Auto());
            root.Controls.Add(Ui.Label("搜索应用名或程序名"), 0, 0); root.Controls.Add(new InputSurface(search), 0, 1); root.Controls.Add(InputSurface.Wrap(list), 0, 2); root.Controls.Add(status, 0, 3);
            list.IsChecked = contains; list.Picked = toggle;
            var bar = Ui.Bar(); bar.Controls.Add(Ui.Button("浏览文件…", delegate
            {
                using (var dialog = new OpenFileDialog { Filter = "程序 (*.exe)|*.exe", Multiselect = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        foreach (string file in dialog.FileNames)
                        {
                            var entry = ProgramCatalog.FromPath(file, null, "手动选择"); if (entry == null) continue;
                            if (!entries.Any(x => String.Equals(x.Key, entry.Key, StringComparison.OrdinalIgnoreCase))) entries.Add(entry);
                            if (!contains(entry.Key)) toggle(entry); list.Pulse(entry.Key);
                        }
                Filter();
            }));
            bar.Controls.Add(Ui.Button("完成", delegate { Close(); })); root.Controls.Add(bar, 0, 4); Controls.Add(root);
            search.TextChanged += delegate { Filter(); }; Shown += delegate
            {
                Ui.Fit(this); search.Focus();
                var worker = new Thread(delegate()
                {
                    try { var found = ProgramCatalog.Scan(foundSoFar => Publish(foundSoFar, false), () => cancelled, warning => catalogWarning = warning); Publish(found, true); }
                    catch (Exception) { Publish(new List<ProgramEntry>(), true); }
                }) { IsBackground = true, Name = "TinyTodo app discovery" };
                worker.SetApartmentState(ApartmentState.STA); worker.Start();
            };
        }
        private void Publish(List<ProgramEntry> found, bool finished)
        {
            if (cancelled) return;
            try { BeginInvoke(new Action(delegate { if (IsDisposed) return; entries = found.Concat(entries).GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(e => e.Name).ToList(); status.Text = entries.Count + " 个程序" + (finished ? (catalogWarning.Length == 0 ? "" : " · " + catalogWarning) + " · 找不到时可先打开该应用，或浏览文件添加" : " · 正在补充 Microsoft Store 应用…"); Filter(); })); }
            catch (InvalidOperationException) { }
        }
        private void Filter() { list.SetRows(entries.Where(e => (e.Name + " " + e.Key).IndexOf(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0)); }
        internal void RefreshChecks() { list.Invalidate(); }
        protected override void Dispose(bool disposing) { cancelled = true; base.Dispose(disposing); }
    }
}
