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
        internal SettingsForm(Store store, DataLocations locations, Action floatingSaved = null)
        {
            Ui.Setup(this, "设置", 600, 320); MinimizeBox = false;
            var root = Ui.Root(Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Fill(), Ui.Auto());
            var path = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
                Height = Ui.U(70), ScrollBars = ScrollBars.Vertical, Text = Path.GetDirectoryName(store.PathName) };
            var bar = Ui.Bar();
            bar.Controls.Add(Ui.Button("选择位置并迁移", delegate
            {
                using (var dialog = new FolderBrowserDialog { Description = "选择父目录；应用将在其中建立 TinyTodo-Data 专用文件夹。", ShowNewFolderButton = true })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    string target = Path.Combine(dialog.SelectedPath, "TinyTodo-Data");
                    if (ThemedDialog.Show(this, "将任务、自动备份和恢复副本迁移到：\n" + target + "\n\n成功后清理旧副本。是否继续？",
                        "迁移数据", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
                    try
                    {
                        string warning = locations.MoveTo(store, dialog.SelectedPath);
                        path.Text = Path.GetDirectoryName(store.PathName);
                        ThemedDialog.Show(this, warning.Length == 0 ? "迁移完成。后续任务保存到新位置。" : warning, "TinyTodo");
                    }
                    catch (Exception ex) { ThemedDialog.Show(this, ex.Message, "无法迁移", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            }));
            bar.Controls.Add(Ui.Button("打开数据文件夹", delegate
            {
                try { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(store.PathName)) { UseShellExecute = true }); }
                catch (Exception ex) { ThemedDialog.Show(this, ex.Message); }
            }));
            root.Controls.Add(Ui.Label("当前任务数据目录"), 0, 0); root.Controls.Add(new TextViewport(path) { Height = Ui.U(76) }, 0, 1);
            root.Controls.Add(bar, 0, 2);
            var floating = Ui.Bar();
            floating.Controls.Add(Ui.Button("悬浮设置", delegate
            {
                using (var dialog = new FloatingSettingsForm(store))
                    if (dialog.ShowDialog(this) == DialogResult.OK && floatingSaved != null) floatingSaved();
            }));
            root.Controls.Add(floating, 0, 3);
            Ui.ScrollRoot(this, root, 220); Shown += delegate { Ui.Fit(this); };
        }
    }

    internal sealed class FloatingSettingsForm : AppWindow
    {
        private readonly Store store;
        internal readonly ComboBox Mode;
        internal readonly TextBox Blacklist;
        internal FloatingSettingsForm(Store store)
        {
            this.store = store;
            Ui.Setup(this, "悬浮设置", 540, 490); MinimizeBox = false;
            var root = Ui.Root(Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto());
            Mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "猫猫默认显示规则" };
            Mode.Items.AddRange(new object[] { "默认隐藏猫猫", "全屏时隐藏猫猫", "默认开启猫猫" });
            Mode.SelectedIndex = store.Current.Window.CatMode == CatVisibilityMode.Hidden ? 0 : store.Current.Window.CatMode == CatVisibilityMode.Always ? 2 : 1;
            Blacklist = new TextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                Height = Ui.U(105), AccessibleName = "程序黑名单", Text = String.Join(Environment.NewLine, (store.Current.Window.CatBlacklist ?? new System.Collections.Generic.List<string>()).Select(n => n + ".exe")) };
            root.Controls.Add(Ui.Label("猫猫默认显示规则"), 0, 0);
            root.Controls.Add(Mode, 0, 1);
            root.Controls.Add(Ui.Label("全屏隐藏只影响猫猫所在屏幕。右键可临时召唤／收起；\n重启应用或保存此设置后恢复默认规则。"), 0, 2);
            root.Controls.Add(Ui.Label("程序黑名单 · 优先级最高\n程序运行期间始终隐藏猫猫，包括后台运行。每行一个程序名。"), 0, 3);
            root.Controls.Add(new TextViewport(Blacklist) { Height = Ui.U(111) }, 0, 4);
            var select = Ui.Bar();
            select.Controls.Add(Ui.Button("选择程序…", delegate
            {
                using (var picker = new OpenFileDialog { Title = "选择要隐藏猫猫的程序", Filter = "程序 (*.exe)|*.exe", Multiselect = true })
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        // Preserve unfinished manual input; validation and deduplication happen on Save.
                        string selected = String.Join(Environment.NewLine, picker.FileNames.Select(Path.GetFileName));
                        Blacklist.Text = Blacklist.Text.TrimEnd() + (String.IsNullOrWhiteSpace(Blacklist.Text) ? "" : Environment.NewLine) + selected;
                    }
            }));
            root.Controls.Add(select, 0, 5);
            root.Controls.Add(Ui.Label("例如 Code.exe。黑名单生效时无法手动召唤；程序退出后自动恢复。"), 0, 6);
            var actions = Ui.Bar();
            var save = Ui.Button("保存", delegate { SaveSettings(); });
            var cancel = Ui.Button("取消", delegate { DialogResult = DialogResult.Cancel; Close(); });
            actions.Controls.Add(save); actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 7);
            CancelButton = cancel;
            Ui.ScrollRoot(this, root, 420); Shown += delegate { Ui.Fit(this); };
        }
        internal bool SaveSettings()
        {
            try
            {
                var names = CatVisibility.ParseBlacklist(Blacklist.Text);
                CatVisibilityMode mode = Mode.SelectedIndex == 0 ? CatVisibilityMode.Hidden : Mode.SelectedIndex == 2 ? CatVisibilityMode.Always : CatVisibilityMode.HideFullscreen;
                store.Change(s => { s.Window.CatMode = mode; s.Window.CatBlacklist = names; });
                DialogResult = DialogResult.OK; Close(); return true;
            }
            catch (Exception ex) { ThemedDialog.Show(this, ex.Message, "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        }
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
                            "src/TaskControls.cs", "src/App.cs", "src/Model.cs", "src/TaskViews.cs", "src/DataLocations.cs", "src/SettingsViews.cs",
                            "src/MarkdownEditor.cs", "src/Widgets.cs", "src/Chrome.cs", "src/Theme.cs", "src/Markdown.cs", "src/Presentation.cs",
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
