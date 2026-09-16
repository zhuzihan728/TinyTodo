using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace TinyTodo
{
    internal sealed class SettingsForm : AppWindow
    {
        private readonly Store store;
        private readonly Action floatingSaved;
        internal readonly ZoneSelector Mode = new ZoneSelector();
        internal readonly ProgramChecklist Blacklist = new ProgramChecklist { Dock = DockStyle.Fill, EmptyText = "尚未添加程序 · 点击下方选择程序" };
        private readonly System.Collections.Generic.Dictionary<string, ProgramEntry> selected = new System.Collections.Generic.Dictionary<string, ProgramEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly System.Collections.Generic.Dictionary<string, ProgramEntry> leaving = new System.Collections.Generic.Dictionary<string, ProgramEntry>(StringComparer.OrdinalIgnoreCase);
        private ProgramPicker picker;
        internal System.Collections.Generic.IEnumerable<string> BlacklistKeys { get { return selected.Keys; } }
        internal SettingsForm(Store store, DataLocations locations, Action floatingSaved = null)
        {
            this.store = store; this.floatingSaved = floatingSaved;
            Ui.Setup(this, "设置", 620, 580);
            var root = Ui.Root(Ui.Auto(), Ui.Auto(), new RowStyle(SizeType.Absolute, Ui.U(155)), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto());
            Mode.Items.AddRange(new object[] { "默认隐藏猫猫", "全屏时隐藏猫猫", "默认开启猫猫" });
            Mode.SelectedItem = Mode.Items[store.Current.Window.CatMode == CatVisibilityMode.Hidden ? 0 : store.Current.Window.CatMode == CatVisibilityMode.Always ? 2 : 1];
            Mode.AccessibleName = "猫猫默认显示规则";
            var modeRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, Ui.U(12)) };
            modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var modeLabel = Ui.Label("猫猫默认显示规则"); modeLabel.Dock = DockStyle.None; modeLabel.Anchor = AnchorStyles.Left; Mode.Anchor = AnchorStyles.Left;
            modeLabel.Margin = new Padding(Ui.U(3), 0, Ui.U(14), 0); Mode.Margin = new Padding(0);
            modeRow.Controls.Add(modeLabel, 0, 0); modeRow.Controls.Add(Mode, 1, 0); root.Controls.Add(modeRow, 0, 0);
            root.Controls.Add(Ui.Label("程序黑名单 · 优先级最高\n这些程序运行时始终隐藏猫猫，取消勾选即可移除。"), 0, 1);
            foreach (string key in store.Current.Window.CatBlacklist ?? new System.Collections.Generic.List<string>())
            {
                string name;
                if (store.Current.Window.CatBlacklistNames == null || !store.Current.Window.CatBlacklistNames.TryGetValue(key, out name)) name = key;
                selected[key] = new ProgramEntry { Name = name, Executable = key + ".exe" };
            }
            Blacklist.IsChecked = key => selected.ContainsKey(key);
            Blacklist.Picked = entry => RemoveBlacklist(entry.Key);
            Blacklist.FeedbackFinished += delegate { leaving.Clear(); RefreshBlacklist(); };
            RefreshBlacklist(); root.Controls.Add(InputSurface.Wrap(Blacklist), 0, 2);
            var choose = Ui.Bar(); choose.Controls.Add(Ui.Button("选择程序…", delegate { OpenPicker(); })); root.Controls.Add(choose, 0, 3);
            var path = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, Height = Ui.U(46), ScrollBars = ScrollBars.Vertical, Text = Path.GetDirectoryName(store.PathName) };
            root.Controls.Add(Ui.Label("当前任务数据目录"), 0, 4); root.Controls.Add(new TextViewport(path) { Height = Ui.U(52) }, 0, 5);
            var bar = Ui.Bar();
            bar.Controls.Add(Ui.Button("选择位置并迁移", delegate
            {
                using (var dialog = new FolderBrowserDialog { Description = "选择父目录；应用将在其中建立 TinyTodo-Data 专用文件夹。", ShowNewFolderButton = true })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    string target = Path.Combine(dialog.SelectedPath, "TinyTodo-Data");
                    string parentDirectory = dialog.SelectedPath;
                    ThemedDialog.Ask(this, store, "将任务、自动备份和恢复副本迁移到：\n" + target + "\n\n成功后清理旧副本。是否继续？", "迁移数据", delegate
                    {
                        try { string warning = locations.MoveTo(store, parentDirectory); path.Text = Path.GetDirectoryName(store.PathName); ThemedDialog.Notify(this, warning.Length == 0 ? "迁移完成。后续任务保存到新位置。" : warning, "TinyTodo"); }
                        catch (Exception ex) { ThemedDialog.Notify(this, ex.Message, "无法迁移"); }
                    });
                }
            }));
            bar.Controls.Add(Ui.Button("打开数据文件夹", delegate
            {
                try { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(store.PathName)) { UseShellExecute = true }); }
                catch (Exception ex) { ThemedDialog.Notify(this, ex.Message); }
            })); root.Controls.Add(bar, 0, 6);
            var actions = Ui.Bar(); actions.Padding = new Padding(Ui.U(12), Ui.U(6), Ui.U(12), Ui.U(12)); var save = Ui.Button("保存", delegate { SaveSettings(); });
            var cancel = Ui.Button("取消", delegate { DialogResult = DialogResult.Cancel; Close(); });
            actions.Controls.Add(save); actions.Controls.Add(cancel); CancelButton = cancel;
            Ui.FixedPage(this, new ScrollSurface(root, Ui.U(420)), actions, false); Shown += delegate { Ui.Fit(this); };
        }
        internal void AddBlacklist(ProgramEntry entry)
        { selected[entry.Key] = entry; leaving.Remove(entry.Key); RefreshBlacklist(); Blacklist.Pulse(entry.Key); if (picker != null && !picker.IsDisposed) picker.RefreshChecks(); }
        internal void RemoveBlacklist(string key)
        {
            if (!selected.ContainsKey(key)) return;
            leaving[key] = selected[key]; selected.Remove(key); Blacklist.Pulse(key); RefreshBlacklist();
            if (picker != null && !picker.IsDisposed) picker.RefreshChecks();
        }
        private void RefreshBlacklist() { Blacklist.SetRows(selected.Values.Concat(leaving.Values).OrderBy(e => e.Name)); }
        private void OpenPicker()
        {
            if (picker != null && !picker.IsDisposed) { TaskWindows.Present(picker, this); return; }
            picker = new ProgramPicker(key => selected.ContainsKey(key), entry => { if (selected.ContainsKey(entry.Key)) RemoveBlacklist(entry.Key); else AddBlacklist(entry); });
            TaskWindows.Show(store, picker, this);
        }
        internal bool SaveSettings()
        {
            try
            {
                var names = selected.Keys.ToList();
                var labels = selected.ToDictionary(pair => pair.Key, pair => pair.Value.Name);
                int index = Mode.Items.IndexOf(Mode.SelectedItem);
                CatVisibilityMode mode = index == 0 ? CatVisibilityMode.Hidden : index == 2 ? CatVisibilityMode.Always : CatVisibilityMode.HideFullscreen;
                store.Change(s => { s.Window.CatMode = mode; s.Window.CatBlacklist = names; s.Window.CatBlacklistNames = labels; });
                if (floatingSaved != null) floatingSaved(); DialogResult = DialogResult.OK; Close(); return true;
            }
            catch (Exception ex) { ThemedDialog.Notify(this, ex.Message, "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        }
        protected override void Dispose(bool disposing) { if (disposing && picker != null) picker.Dispose(); base.Dispose(disposing); }
    }

    internal static class Uninstaller
    {
        internal const string PackageMarker = "TinyTodo application package v2";
        internal static int CleanupData()
        {
            try
            {
                using (var mutex = new Mutex(false, @"Local\TinyTodo.2026.v1"))
                {
                    if (!Acquire(mutex)) throw new InvalidOperationException("请先从托盘退出 TinyTodo，再清理任务文件。");
                    try { var locations = DataLocations.Default(); locations.Load(true); locations.RemoveOwnedData(); }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("任务文件清理未完成：\n" + ex.Message + "\n已保留数据位置记录，请解决问题后重试。", "TinyTodo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }

        internal static int Run(string applicationDirectory)
        {
            try
            {
                string root = Path.GetFullPath(applicationDirectory);
                string marker = Path.Combine(root, ".tinytodo-app");
                if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != PackageMarker)
                    throw new InvalidOperationException("无法确认应用目录，未执行卸载。请从完整解压的新版文件夹运行 uninstall.cmd。");
                // Read location info without mutating it before the user's confirmation.
                var locations = DataLocations.Default();
                string registry = Path.Combine(locations.MetadataDirectory, "data-locations.json");
                string display = locations.MetadataDirectory;
                if (File.Exists(registry))
                {
                    var record = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<LocationRecord>(File.ReadAllText(registry));
                    if (record == null || record.KnownDirectories == null) throw new InvalidOperationException("数据位置配置无法读取，请保留文件并先修复配置。");
                    display = String.Join("\n", record.KnownDirectories);
                }
                string question = "将退出 TinyTodo，并永久删除本目录内的程序文件及以下位置中应用创建的任务、备份和配置：\n\n程序：" + root +
                    "\n\n数据：\n" + display + "\n\n此操作不可撤销。你另放的文件和手动复制到其他位置的备份会保留。\n是否卸载？";
                if (MessageBox.Show(question, "卸载 TinyTodo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return 2;
                using (var mutex = new Mutex(false, @"Local\TinyTodo.2026.v1"))
                {
                    bool owned = Acquire(mutex);
                    if (!owned)
                    {
                        Program.PostMessage(new IntPtr(0xffff), Program.ExitMessage, IntPtr.Zero, IntPtr.Zero);
                        for (int i = 0; i < 100 && !owned; i++) { Thread.Sleep(100); owned = Acquire(mutex); }
                    }
                    if (!owned) throw new InvalidOperationException("TinyTodo 仍在运行。请从托盘退出（旧版需手动退出），再运行卸载。未删除文件。");
                    try
                    {
                        locations.Load(true); locations.RemoveOwnedData();
                        // No recursive folder deletion: a user may have extracted onto Desktop.
                        string[] files = {
                            "src/AssemblyInfo.cs", "src/TaskControls.cs", "src/App.cs", "src/Model.cs", "src/TaskViews.cs", "src/DataLocations.cs", "src/SettingsViews.cs",
                            "src/TaskWindows.cs", "src/ProgramCatalog.cs", "src/MarkdownEditor.cs", "src/Widgets.cs", "src/Chrome.cs", "src/Theme.cs", "src/Markdown.cs", "src/Presentation.cs",
                            "assets/app-icon.png", "assets/floating-icon.png", "assets/floating-icon.gif", "assets/cat-toggle.png", "assets/app.ico", "assets/WenYuanRoundedSC-Regular.ttf", "assets/WenYuanRoundedSC-Bold.ttf", "assets/Font-coverage.txt", "assets/Font-source-notice.txt", "assets/Font-license.txt",
                            "tests/CoreTests.cs", "tests/UiTests.cs", "bin/TinyTodo.exe", "bin/TinyTodo.Tests.exe", "bin/TinyTodo.UiTests.exe", "bin/version.txt",
                            "app.manifest", "build.cmd", "start.cmd", "test.cmd", "ui-test.cmd", "uninstall.cmd",
                            "README.md", "START-HERE.txt", "VALIDATION.txt", "UPGRADE.txt", "DESIGN.md", ".tinytodo-app"
                        };
                        foreach (string relative in files)
                        {
                            string file = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                            if (File.Exists(file)) File.Delete(file);
                        }
                        foreach (string name in new string[] { "src", "tests", "bin", "assets" })
                        {
                            string dir = Path.Combine(root, name);
                            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                        }
                        bool leftovers = Directory.EnumerateFileSystemEntries(root).Any();
                        if (!leftovers) Directory.Delete(root);
                        MessageBox.Show("TinyTodo 程序和已记录的数据已清理。" +
                            (leftovers ? "\n程序目录中还有其他文件，已保留：\n" + root : "") +
                            "\n下载的 ZIP、旧版程序副本、手动备份和你自行创建的快捷方式不会自动删除。", "卸载完成");
                        return 0;
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex) { MessageBox.Show("卸载未完成：\n" + ex.Message + "\n请保留剩余文件，解决问题后重试。", "TinyTodo", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1; }
        }
        private static bool Acquire(Mutex mutex)
        { try { return mutex.WaitOne(0); } catch (AbandonedMutexException) { return true; } }
    }
}
