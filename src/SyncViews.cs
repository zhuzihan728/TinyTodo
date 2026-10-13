using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TinyTodo
{
    internal sealed class SyncController : IDisposable
    {
        private readonly Store store;
        private readonly string cacheFile;
        private readonly BackgroundWorker worker = new BackgroundWorker();
        private readonly Timer delay = new Timer { Interval = 600 };
        private readonly Func<string, string, ISyncRemote> connect;
        private string repository, variable, observed, synced;
        private int generation;
        private bool started, applying, disposed, failed;
        private SyncAction? pending;
        private Action exitWhenIdle;
        internal Func<SyncData, bool> CanApply;
        internal event EventHandler Changed;
        internal bool Busy { get { return worker.IsBusy || active != null; } }
        internal bool Conflict { get; private set; }
        internal string Status { get; private set; }
        private sealed class Work
        {
            internal string Repository, Variable;
            internal int Generation;
            internal SyncAction Action;
            internal SyncData Snapshot;
            internal SyncResult Result;
        }
        internal SyncController(Store store, DataLocations locations, Control dispatcher, Func<string, string, ISyncRemote> connect = null)
        {
            this.store = store; cacheFile = Path.Combine(locations.MetadataDirectory, "sync-state.json");
            this.connect = connect ?? ((repo, env) => new GitHubData(repo, GitHubLogin.Token(env)));
            repository = store.Current.Window.SyncRepository ?? ""; variable = store.Current.Window.SyncTokenVariable ?? "";
            observed = SyncData.Capture(store.Current).Encode(); Status = repository.Length == 0 ? "未启用同步" : "等待启动同步";
            store.Changed += StoreChanged;
            delay.Tick += delegate { delay.Stop(); Begin(); };
            worker.DoWork += delegate(object sender, DoWorkEventArgs e)
            {
                var work = (Work)e.Argument;
                var baseline = SyncCache.Load(cacheFile, work.Repository);
                work.Result = SyncExchange.Run(this.connect(work.Repository, work.Variable), baseline, work.Snapshot, work.Action);
                e.Result = work;
            };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                if (disposed) return;
                try { dispatcher.BeginInvoke(new Action(delegate { Completed(sender, e); })); }
                catch (InvalidOperationException) { if (!disposed) throw; }
            };
        }
        internal void Start() { if (started) return; started = true; if (repository.Length != 0) Request(SyncAction.Pull); }
        internal void Request(SyncAction action)
        {
            if (disposed) return;
            if (repository.Length == 0) { SetStatus("请先填写并保存仓库链接。", false); return; }
            pending = action; failed = false; Conflict = false; delay.Stop(); Begin();
        }
        private void StoreChanged(object sender, EventArgs e)
        {
            if (applying || disposed) return;
            string repo = store.Current.Window.SyncRepository ?? "", env = store.Current.Window.SyncTokenVariable ?? "";
            string data = SyncData.Capture(store.Current).Encode();
            bool configChanged = repository != repo || variable != env;
            if (configChanged)
            {
                repository = repo; variable = env; generation++; synced = null; failed = Conflict = false; pending = null;
                SetStatus(repo.Length == 0 ? "未启用同步" : "等待连接 GitHub", false);
            }
            bool dataChanged = observed != data; observed = data;
            if (!started || repo.Length == 0) return;
            if (configChanged) pending = SyncAction.Pull;
            else if (dataChanged && !Conflict) pending = pending ?? SyncAction.Push;
            if ((configChanged || dataChanged) && pending.HasValue) { delay.Stop(); delay.Start(); }
        }
        private Work active;
        private void Begin()
        {
            if (disposed || Busy || !pending.HasValue || repository.Length == 0) return;
            active = new Work { Repository = repository, Variable = variable, Generation = generation, Action = pending.Value, Snapshot = SyncData.Capture(store.Current) };
            pending = null; SetStatus(active.Action == SyncAction.Pull || active.Action == SyncAction.KeepRemote ? "正在 Pull…" : "正在 Push…", false);
            worker.RunWorkerAsync(active); RaiseChanged();
        }
        private void Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            if (disposed) return;
            Work work = active; active = null;
            if (work.Generation != generation) { if (pending.HasValue) Begin(); else FinishExit(); return; }
            try
            {
                if (e.Error != null)
                {
                    if (e.Error is SyncException || e.Error is IOException || e.Error is UnauthorizedAccessException) { Failed(e.Error); return; }
                    throw new InvalidOperationException("同步发生未预期错误。", e.Error);
                }
                var current = SyncData.Capture(store.Current);
                if (work.Action == SyncAction.KeepRemote && !current.Same(work.Snapshot)) throw new SyncException("Pull 期间本机任务发生了变化，未覆盖。请重新操作。");
                var next = SyncData.Merge(work.Snapshot, current, work.Result.Data);
                if (!next.Same(current))
                {
                    var focused = store.Focus.TaskId;
                    if (focused != null && !next.Tasks.Any(t => t.Id == focused && !t.Done)) throw new SyncException("云端修改涉及正在专注的任务，请结束专注后再同步。");
                    if (CanApply != null && !CanApply(next)) throw new SyncException("云端修改涉及正在编辑的任务，请保存或关闭编辑窗口后再 Pull。");
                    applying = true;
                    try { store.Change(next.Apply); }
                    finally { applying = false; }
                }
                SyncCache.Save(cacheFile, work.Repository, work.Result.Baseline);
                synced = work.Result.Baseline.Encode(); observed = SyncData.Capture(store.Current).Encode(); failed = false;
                SetStatus("已同步 · " + DateTime.Now.ToString("HH:mm:ss"), false);
                if (synced != observed) pending = pending ?? SyncAction.Push;
                if (pending.HasValue) { delay.Stop(); delay.Start(); } else FinishExit();
            }
            catch (SyncException ex) { Failed(ex); }
            catch (IOException ex) { Failed(ex); }
            catch (UnauthorizedAccessException ex) { Failed(ex); }
        }
        private void Failed(Exception error)
        {
            pending = null; delay.Stop(); failed = true;
            var syncError = error as SyncException;
            SetStatus("同步未完成：" + error.Message, syncError != null && syncError.Conflict); FinishExit();
        }
        private void SetStatus(string status, bool conflict) { Status = status; Conflict = conflict; RaiseChanged(); }
        private void RaiseChanged() { if (Changed != null) Changed(this, EventArgs.Empty); }
        internal bool FinishBeforeExit(Action complete)
        {
            if (!started || repository.Length == 0 || (failed && !Busy) || (!Busy && synced == SyncData.Capture(store.Current).Encode())) return false;
            exitWhenIdle = complete; Request(SyncAction.Push); return true;
        }
        private void FinishExit() { var done = exitWhenIdle; exitWhenIdle = null; if (done != null) done(); }
        public void Dispose()
        { disposed = true; store.Changed -= StoreChanged; delay.Dispose(); worker.Dispose(); }
    }
    internal sealed class SyncSettings : Panel
    {
        private readonly Store store;
        private readonly SyncController sync;
        internal readonly TextBox Repository = new TextBox { Dock = DockStyle.Top, AccessibleName = "GitHub 同步仓库", MaxLength = 300 };
        internal readonly TextBox Variable = new TextBox { Dock = DockStyle.Top, AccessibleName = "GitHub token 环境变量名", MaxLength = 120 };
        private readonly Label status = Ui.Label("");
        internal readonly Button Push, Pull;
        private readonly FlowLayoutPanel resolve;
        internal SyncSettings(Store store, SyncController sync)
        {
            this.store = store; this.sync = sync; AutoSize = true; Dock = DockStyle.Top;
            var root = Ui.Root(Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto()); root.AutoSize = true; root.Padding = Padding.Empty; root.Dock = DockStyle.Top;
            Repository.Text = store.Current.Window.SyncRepository ?? ""; Variable.Text = store.Current.Window.SyncTokenVariable ?? "";
            root.Controls.Add(Ui.Label("GitHub 数据同步 · 私有仓库"), 0, 0);
            root.Controls.Add(new InputSurface(Repository), 0, 1);
            root.Controls.Add(Ui.Label("Token 环境变量名 · 留空使用本机 Git 登录"), 0, 2);
            root.Controls.Add(new InputSurface(Variable), 0, 3);
            var bar = Ui.Bar();
            Push = Ui.Button("Push", delegate { Run(SyncAction.Push); }); Pull = Ui.Button("Pull", delegate { Run(SyncAction.Pull); });
            bar.Controls.Add(Push); bar.Controls.Add(Pull); Ui.CenterRow(bar); root.Controls.Add(bar, 0, 4);
            status.MaximumSize = new Size(Ui.U(510), 0); root.Controls.Add(status, 0, 5);
            resolve = Ui.Bar();
            resolve.Controls.Add(Ui.Button("采用本机", delegate { Resolve(SyncAction.KeepLocal); }));
            resolve.Controls.Add(Ui.Button("采用云端", delegate { Resolve(SyncAction.KeepRemote); })); Ui.CenterRow(resolve); root.Controls.Add(resolve, 0, 6);
            var help = Ui.Label("保存或 Push/Pull 后启用：启动 Pull，更新 Push。\n清空仓库链接并保存可停用同步。"); help.ForeColor = Theme.Muted; root.Controls.Add(help, 0, 7);
            Controls.Add(root); if (sync != null) sync.Changed += SyncChanged; RefreshStatus();
        }
        internal void ValidateSettings()
        {
            if (Repository.Text.Trim().Length != 0) GitHubLogin.Repository(Repository.Text);
            GitHubLogin.Variable(Variable.Text);
            if (sync != null && sync.Busy && (Repository.Text.Trim() != (store.Current.Window.SyncRepository ?? "") || Variable.Text.Trim() != (store.Current.Window.SyncTokenVariable ?? "")))
                throw new SyncException("同步进行中，请稍后再修改仓库或授权方式。");
        }
        internal void Apply(Settings settings)
        {
            settings.SyncRepository = Repository.Text.Trim().Length == 0 ? "" : GitHubLogin.Repository(Repository.Text);
            settings.SyncTokenVariable = GitHubLogin.Variable(Variable.Text);
        }
        private void Run(SyncAction action)
        {
            try { ValidateSettings(); store.Change(s => Apply(s.Window)); sync.Request(action); }
            catch (InvalidOperationException ex) { ThemedDialog.Notify(FindForm(), ex.Message, "同步设置"); }
            catch (IOException ex) { ThemedDialog.Notify(FindForm(), ex.Message, "无法保存同步设置"); }
            catch (UnauthorizedAccessException ex) { ThemedDialog.Notify(FindForm(), ex.Message, "无法保存同步设置"); }
        }
        private void Resolve(SyncAction action)
        {
            string text = action == SyncAction.KeepLocal ? "以本机全部任务和专注记录替换云端版本？云端旧版本保留在 GitHub 提交历史中。" : "以云端全部任务和专注记录替换本机版本？当前本机版本会保存为 tasks.json.bak。";
            ThemedDialog.Ask(FindForm(), store, text, "处理同步冲突", delegate { Run(action); });
        }
        private void SyncChanged(object sender, EventArgs e) { RefreshStatus(); }
        private void RefreshStatus()
        {
            status.Text = sync == null ? "保存后将在主窗口启动同步" : sync.Status;
            status.ForeColor = sync != null && sync.Status.StartsWith("同步未完成", StringComparison.Ordinal) ? Theme.Rose : Theme.Muted;
            Push.Enabled = Pull.Enabled = sync != null && !sync.Busy;
            resolve.Visible = sync != null && sync.Conflict; foreach (Control c in resolve.Controls) c.Enabled = sync != null && !sync.Busy;
            if (Parent != null) Parent.PerformLayout();
        }
        protected override void OnLayout(LayoutEventArgs e)
        { if (status != null) status.MaximumSize = new Size(Math.Max(1, ClientSize.Width - Ui.U(12)), 0); base.OnLayout(e); }
        protected override void Dispose(bool disposing) { if (disposing && sync != null) sync.Changed -= SyncChanged; base.Dispose(disposing); }
    }
}
