using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace TinyTodo
{
    internal static class TaskActions
    {
        private static readonly ConditionalWeakTable<Control, TaskActionMenu> menus = new ConditionalWeakTable<Control, TaskActionMenu>();
        private sealed class TaskActionMenu : SoftMenu
        {
            private Form owner;
            private Store store;
            private string id;
            private Action refresh;
            private readonly ToolStripMenuItem important;
            internal TaskActionMenu(Control source)
            {
                Renderer = new TaskMenuRenderer();
                BackColor = Theme.Canvas; ForeColor = Theme.Ink; Font = Theme.Font(9.5F, FontStyle.Regular, "任务列表"); ShowImageMargin = false;
                Items.Add("编辑", null, delegate { Run(4); });
                Items.Add("添加前置", null, delegate { Run(0); });
                Items.Add("添加后续", null, delegate { Run(1); });
                important = new ToolStripMenuItem("设为！", null, delegate { Run(3); }); important.Image = null;
                Items.Add(important);
                Items.Add(new ToolStripSeparator());
                Items.Add("永久删除", null, delegate { Run(2); });
                // Reuse until the source is disposed; never destroy items in Closed.
                source.Disposed += delegate { Dispose(); };
            }
            internal void Bind(Form form, Store data, string taskId, Action update)
            {
                owner = form; store = data; id = taskId; refresh = update;
                important.Text = Rules.Get(data.Current, taskId).Important ? "取消！" : "设为！";
                important.AccessibleName = important.Text + "重要标记";
            }
            private void Run(int action)
            {
                // Finish menu dismissal before opening a dialog or deleting its owner.
                Form form = owner; Store data = store; string taskId = id; Action update = refresh;
                if (form.IsDisposed || form.Disposing || !form.IsHandleCreated) return;
                form.BeginInvoke(new Action(delegate
                {
                    if (form.IsDisposed || form.Disposing || !data.Current.Tasks.Any(t => t.Id == taskId)) return;
                    if (action == 4) Edit(form, data, taskId, update);
                    else if (action == 3) ToggleImportant(form, data, taskId, update);
                    else if (action == 2) Delete(form, data, taskId, update);
                    else AddRelated(form, data, taskId, action == 0, update);
                }));
            }
        }
        internal static ContextMenuStrip Menu(Form owner, Control source, Point point, Store store, string id, Action refresh)
        {
            TaskActionMenu menu = menus.GetValue(source, control => new TaskActionMenu(control));
            menu.Bind(owner, store, id, refresh);
            menu.Show(source, point);
            return menu;
        }
        internal static void AddRelated(Form owner, Store store, string id, bool before, Action refresh)
        {
            using (var editor = new TaskEditor(store, null, id, before))
            { editor.TopMost = owner.TopMost; if (editor.ShowDialog(owner) == DialogResult.OK && !owner.IsDisposed) refresh(); }
        }
        internal static void Edit(Form owner, Store store, string id, Action refresh)
        {
            using (var editor = new TaskEditor(store, Rules.Get(store.Current, id)))
            { editor.TopMost = owner.TopMost; if (editor.ShowDialog(owner) == DialogResult.OK && !owner.IsDisposed) refresh(); }
        }
        internal static void ToggleImportant(Form owner, Store store, string id, Action refresh)
        {
            try { store.Change(s => { Todo t = Rules.Get(s, id); t.Important = !t.Important; }); }
            catch (Exception ex) { ThemedDialog.Show(owner, ex.Message, "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            if (!owner.IsDisposed) refresh();
        }
        internal static void Delete(Form owner, Store store, string id, Action refresh)
        {
            Todo t = Rules.Get(store.Current, id);
            int count = store.Current.Tasks.Count(x => x.Prerequisites.Contains(id));
            string prompt = "永久删除“" + t.Name + "”？" + (count > 0 ? "\n" + count + " 个后续任务将解除这项前置关系。" : "") + "\n此操作无法撤销。";
            if (ThemedDialog.Show(owner, prompt, "永久删除", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            try
            {
                string warning = DeleteConfirmed(store, id);
                if (warning.Length > 0) ThemedDialog.Show(warning, "TinyTodo");
            }
            catch (Exception ex) { ThemedDialog.Show("删除失败，任务仍保留：\n" + ex.Message, "TinyTodo", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            if (!owner.IsDisposed) refresh();
        }
        internal static string DeleteConfirmed(Store store, string id)
        {
            // Close matching previews before the durable transaction, including the caller.
            foreach (var preview in Application.OpenForms.Cast<Form>().OfType<TaskPreview>().Where(p => p.CurrentId == id).ToArray()) preview.Close();
            store.Change(s => Rules.Delete(s, id));
            try { if (File.Exists(store.PathName + ".bak")) File.Delete(store.PathName + ".bak"); }
            catch (Exception ex) { return "任务已删除，自动备份清理失败：\n" + ex.Message; }
            return "";
        }
    }

    internal sealed class TaskPreview : AppWindow
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendTitleMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        private void ClearTitleMargins()
        { if (title.IsHandleCreated) SendTitleMessage(title.Handle, 0xD3, new IntPtr(3), IntPtr.Zero); }

        private readonly Store store;
        private string id;
        internal string CurrentId { get { return id; } }
        private readonly Stack<string> back = new Stack<string>();
        private readonly TextBox title;
        private readonly MarkdownView description;
        private readonly ReadOnlyText meta, counts;
        private readonly Button done;
        private readonly Button important;
        private readonly Panel titleIcon;
        private readonly Bitmap importantIcon = Ui.ImportantImage();
        private readonly System.Windows.Forms.Timer dateTimer;
        private DateTime today = DateTime.Today;
        private readonly TaskViews views;
        private bool rendering, queued;
        internal TaskPreview(Store store, string id)
        {
            this.store = store; this.id = id;
            Ui.Setup(this, "任务预览", 740, 620);
            var root = new PreviewLayout { Dock = DockStyle.Fill };
            var bar = new Panel();
            var left = Ui.Bar(); left.Controls.Add(Ui.Button("←", delegate { GoBack(); }));
            left.Controls.Add(Ui.Button("编辑", delegate { Edit(); }));
            done = Ui.Button("完成", delegate { if (!rendering) Queue(delegate { Toggle(this.id); }); });
            left.Controls.Add(done);
            important = Ui.Button("设为", delegate { Queue(delegate { TaskActions.ToggleImportant(this, store, this.id, Render); }); });
            important.Image = importantIcon; important.TextImageRelation = TextImageRelation.TextBeforeImage;
            left.Controls.Add(important);
            var delete = Ui.Button("永久删除", delegate { Queue(delegate { TaskActions.Delete(this, store, this.id, Render); }); });
            delete.ForeColor = Theme.Rose; delete.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            bar.Controls.Add(left); bar.Controls.Add(delete);
            title = new ReadOnlyText { Dock = DockStyle.Fill, Margin = Padding.Empty, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
                Font = Theme.Font(14F, FontStyle.Bold, "任务预览"), ForeColor = Theme.Ink, BackColor = BackColor, ScrollBars = ScrollBars.None };
            title.HandleCreated += delegate { ClearTitleMargins(); };
            title.FontChanged += delegate { ClearTitleMargins(); };
            var titleRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            titleIcon = new Panel { Margin = Padding.Empty, AccessibleName = "重要任务" };
            titleIcon.Paint += delegate(object sender, PaintEventArgs e)
            {
                // Match the list marker's shape and spacing, scaled with the title text.
                float ratio = title.Font.SizeInPoints / 9.5F;
                float height = Ui.U(13) * ratio;
                Ui.DrawImportant(e.Graphics, new RectangleF(0, (title.Font.Height - height) / 2F, Ui.U(9) * ratio, height));
            };
            titleRow.Controls.Add(titleIcon, 0, 0); titleRow.Controls.Add(title, 1, 0);
            meta = new ReadOnlyText { Font = Font, BackColor = BackColor }; counts = new ReadOnlyText { Font = Font, BackColor = BackColor }; meta.ForeColor = counts.ForeColor = Theme.Muted;
            description = new MarkdownView { BackColor = BackColor };
            views = new TaskViews(true);
            MinimumSize = SizeFromClientSize(new Size(views.First.RequiredWidth + Ui.U(64), Ui.U(460)));
            foreach (var table in new TaskTable[] { views.First, views.Second })
            {
                table.OpenTask = Navigate;
                table.SortDate = delegate { Queue(delegate { SavePreferences(s => s.DateDescending = !s.DateDescending); }); };
                table.ToggleDateMode = delegate { Queue(delegate { SavePreferences(s => s.DateCountdown = !s.DateCountdown); }); };
                table.ToggleTask = delegate(string taskId) { Queue(delegate { Toggle(taskId); }); };
                var source = table;
                table.TaskMenu = delegate(string taskId, Point point) { TaskActions.Menu(this, source, point, store, taskId, Render); };
            }
            views.First.AddTask = delegate { Queue(delegate { TaskActions.AddRelated(this, store, this.id, true, Render); }); };
            views.Second.AddTask = delegate { Queue(delegate { TaskActions.AddRelated(this, store, this.id, false, Render); }); };
            views.Graph.OpenTask = Navigate;
            views.Graph.ToggleTask = delegate(string taskId) { Queue(delegate { Toggle(taskId); }); };
            views.Graph.TaskMenu = delegate(string taskId, Point point) { TaskActions.Menu(this, views.Graph, point, store, taskId, Render); };
            root.Bind(bar, left, delete, titleRow, meta, description, counts, views);
            Controls.Add(root); KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (Ui.ExactModifiers(e, Keys.None) && e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
                else if (Ui.ExactModifiers(e, Keys.Alt) && e.KeyCode == Keys.Left) { e.SuppressKeyPress = true; GoBack(); }
            };
            dateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            dateTimer.Tick += delegate { if (today != DateTime.Today) { today = DateTime.Today; Render(); } }; dateTimer.Start();
            Shown += delegate { Ui.Fit(this); }; Render();
        }
        private void Queue(Action action)
        {
            if (queued || IsDisposed) return; queued = true;
            BeginInvoke(new Action(delegate { try { if (!IsDisposed) action(); } finally { queued = false; } }));
        }
        internal void GoBack()
        {
            while (back.Count > 0)
            {
                string previous = back.Pop();
                if (store.Current.Tasks.Any(t => t.Id == previous)) { id = previous; Render(); return; }
            }
            Close();
        }
        internal void Navigate(string next)
        {
            if (next == null || next == id) return;
            Queue(delegate { back.Push(id); id = next; Render(); });
        }
        private void Render()
        {
            if (IsDisposed) return;
            Todo t = store.Current.Tasks.FirstOrDefault(x => x.Id == id); if (t == null) { Close(); return; }
            rendering = true;
            try
            {
                title.Text = t.Name; title.Font = Theme.Font(14F, FontStyle.Bold, t.Name); description.ShowMarkdown(t.Description);
                titleIcon.Size = new Size((int)Math.Round(Ui.U(12) * title.Font.SizeInPoints / 9.5F), title.Font.Height);
                titleIcon.Invalidate();
                meta.Text = Ui.Status(store.Current, t) + (t.Due == null ? "" : " · " + t.Due + " · " + Rules.Countdown(t.Due, DateTime.Today));
                titleIcon.Visible = t.Important;
                important.Text = t.Important ? "取消" : "设为"; important.AccessibleName = important.Text + "重要标记";
                done.Text = t.Done ? "恢复待办" : "完成";
                counts.Text = "前置 " + t.Prerequisites.Count + " 项 · 后续 " + store.Current.Tasks.Count(x => x.Prerequisites.Contains(id)) + " 项";
                views.Render(store.Current, id, null); Text = "任务预览 · " + t.Name;
            }
            finally { rendering = false; }
        }
        private void Edit()
        {
            using (var editor = new TaskEditor(store, Rules.Get(store.Current, id)))
            { editor.TopMost = TopMost; if (editor.ShowDialog(this) == DialogResult.OK) Render(); }
        }
        private void Toggle(string target)
        {
            if (views.HasCompletionFeedback(target)) return;
            try
            {
                store.Change(s => { if (Rules.Get(s, target).Done) Rules.Restore(s, target); else Rules.Complete(s, target); });
                views.ShowCompletionFeedback(store.Current, target);
            }
            catch (Exception ex) { ThemedDialog.Show(this, ex.Message, "TinyTodo", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            Render();
        }
        private void SavePreferences(Action<Settings> update)
        {
            try { store.Change(s => update(s.Window)); }
            catch (Exception ex) { ThemedDialog.Show(this, ex.Message, "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            Render();
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { if (dateTimer != null) dateTimer.Dispose(); importantIcon.Dispose(); } base.Dispose(disposing); }
    }

    internal sealed class TaskEditor : AppWindow
    {
        private readonly Store store;
        private readonly string id, anchor;
        private readonly bool before;
        private readonly TextBox nameBox, descriptionBox;
        private readonly DateInput date;
        private readonly TaskChoices prerequisites;
        private sealed class Choice
        {
            public string Id; public string Text; public bool Important;
            public override string ToString() { return Important ? "重要 · " + Text : Text; }
        }
        private sealed class TaskChoices : Control
        {
            internal readonly List<Choice> Items = new List<Choice>();
            private readonly HashSet<int> chosen = new HashSet<int>();
            private readonly ThinScroll scroll = new ThinScroll();
            private int first, selected = -1; internal int ItemHeight = Ui.U(30);
            internal event ItemCheckEventHandler ItemCheck;
            internal IEnumerable<Choice> CheckedItems { get { return Items.Where((x, i) => chosen.Contains(i)); } }
            internal int AddChoice(Choice value) { Items.Add(value); Sync(); return Items.Count - 1; }
            internal bool GetItemChecked(int index) { return chosen.Contains(index); }
            internal void SetItemChecked(int index, bool value)
            {
                var e = new ItemCheckEventArgs(index, value ? CheckState.Checked : CheckState.Unchecked, chosen.Contains(index) ? CheckState.Checked : CheckState.Unchecked);
                if (ItemCheck != null) ItemCheck(this, e); if (e.NewValue == CheckState.Checked) chosen.Add(index); else chosen.Remove(index); Invalidate();
            }
            internal TaskChoices() { DoubleBuffered = true; TabStop = true; BackColor = Color.White; ForeColor = Theme.Ink; scroll.Dock = DockStyle.Right; Controls.Add(scroll); scroll.Changed = value => { first = value; Invalidate(); }; }
            private int Page { get { return Math.Max(1, Height / ItemHeight); } }
            private void Sync() { first = Math.Max(0, Math.Min(first, Items.Count - Page)); scroll.SetRange(Math.Max(0, Items.Count - Page), Page, first); }
            protected override void OnResize(EventArgs e) { base.OnResize(e); if (scroll != null) Sync(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Color.White); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                for (int i = first; i < Math.Min(Items.Count, first + Page + 1); i++)
                {
                    Choice choice = Items[i]; var r = new Rectangle(1, (i - first) * ItemHeight + 2, Width - Ui.U(13), ItemHeight - 4);
                    if (i == selected) using (var path = Theme.Rounded(r, Ui.U(6))) using (var brush = new SolidBrush(Theme.RoseSoft)) e.Graphics.FillPath(brush, path);
                    int left = Ui.U(8), size = Ui.U(14); Theme.Check(e.Graphics, new RectangleF(left, r.Top + (r.Height - size) / 2, size, size), chosen.Contains(i)); left += Ui.U(23);
                    if (choice.Important) { Ui.DrawImportant(e.Graphics, new RectangleF(left, r.Top + (r.Height - Ui.U(13)) / 2F, Ui.U(9), Ui.U(13))); left += Ui.U(12); }
                    TextRenderer.DrawText(e.Graphics, choice.Text, Font, new Rectangle(left, r.Top, Math.Max(1, r.Right - left), r.Height), Enabled ? Theme.Ink : Theme.Muted,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                }
            }
            protected override void OnMouseDown(MouseEventArgs e)
            { base.OnMouseDown(e); Focus(); if (!Enabled || e.Button != MouseButtons.Left) return; int index = first + e.Y / ItemHeight; if (index >= 0 && index < Items.Count) { selected = index; SetItemChecked(index, !chosen.Contains(index)); } }
            protected override void OnMouseWheel(MouseEventArgs e) { first -= e.Delta / 120 * 3; Sync(); Invalidate(); base.OnMouseWheel(e); }
            protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || base.IsInputKey(key); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (!Ui.ExactModifiers(e, Keys.None)) { base.OnKeyDown(e); return; }
                if (e.KeyCode == Keys.Space && selected >= 0 && selected < Items.Count) SetItemChecked(selected, !chosen.Contains(selected));
                else if (e.KeyCode == Keys.Up) selected = Math.Max(0, selected - 1);
                else if (e.KeyCode == Keys.Down) selected = Math.Min(Items.Count - 1, selected + 1); else { base.OnKeyDown(e); return; }
                if (selected < first) first = selected; if (selected >= first + Page) first = selected - Page + 1; Sync(); Invalidate(); e.SuppressKeyPress = true;
            }
        }
        internal TaskEditor(Store store, Todo task) : this(store, task, null, false) { }
        internal TaskEditor(Store store, Todo task, string anchor, bool before)
        {
            this.anchor = anchor; this.before = before;
            this.store = store; id = task == null ? null : task.Id;
            Ui.Setup(this, task == null ? (anchor == null ? "添加任务" : before ? "添加前置" : "添加后续") : "编辑任务", 600, 710);
            MinimizeBox = false;
            var root = Ui.Root(Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(),
                new RowStyle(SizeType.Absolute, Ui.U(210)), Ui.Auto(), Ui.Fill(), Ui.Auto());
            nameBox = new TextBox { Dock = DockStyle.Top, MaxLength = 120, Text = task == null ? "" : task.Name };
            date = new DateInput { Dock = DockStyle.Top };
            if (task != null && task.Due != null)
                date.Value = DateTime.ParseExact(task.Due, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            date.Checked = task != null && task.Due != null;
            descriptionBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true,
                MaxLength = 10000, ScrollBars = ScrollBars.Vertical, Text = task == null ? "" : task.Description };
            var descriptionTabs = new MarkdownEditor(descriptionBox);
            prerequisites = new TaskChoices { Dock = DockStyle.Fill, Enabled = task == null || !task.Done };
            prerequisites.ItemHeight = Math.Max(Font.Height + Ui.U(8), Ui.U(26));
            var excluded = anchor != null && before ? new HashSet<string>(Rules.Related(store.Current, anchor, false).Keys) : new HashSet<string>();
            if (anchor != null && before) excluded.Add(anchor);
            foreach (Todo t in store.Current.Tasks.Where(x => x.Id != id && !excluded.Contains(x.Id)).OrderBy(x => x.Done).ThenBy(x => x.CreatedAt))
            {
                int index = prerequisites.AddChoice(new Choice { Id = t.Id, Important = t.Important, Text = (t.Done ? "[已完成] " : "[待办] ") +
                    t.Name });
                if ((task != null && task.Prerequisites.Contains(t.Id)) || (anchor == t.Id && !before)) prerequisites.SetItemChecked(index, true);
            }
            prerequisites.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                var choice = prerequisites.Items[e.Index] as Choice;
                if (anchor != null && !before && choice != null && choice.Id == anchor) e.NewValue = CheckState.Checked;
            };
            var actions = Ui.Bar();
            var save = Ui.Button("保存", delegate { Save(); }); save.BackColor = Theme.RoseSoft; save.ForeColor = Theme.Ink;
            var cancel = Ui.Button("取消", delegate { DialogResult = DialogResult.Cancel; Close(); });
            actions.Controls.AddRange(new Control[] { save, cancel }); AcceptButton = save; CancelButton = cancel;
            root.Controls.Add(Ui.Label("任务名称 *"), 0, 0); root.Controls.Add(new InputSurface(nameBox), 0, 1);
            root.Controls.Add(Ui.Label("日期"), 0, 2); root.Controls.Add(date, 0, 3);
            root.Controls.Add(Ui.Label("Markdown"), 0, 4); root.Controls.Add(descriptionTabs, 0, 5);
            root.Controls.Add(Ui.Label(task != null && task.Done ? "前置任务（已完成）" : "前置任务"), 0, 6);
            root.Controls.Add(InputSurface.Wrap(prerequisites), 0, 7); root.Controls.Add(actions, 0, 8); Ui.ScrollRoot(this, root, 650);
            Shown += delegate { Ui.Fit(this); nameBox.Focus(); nameBox.SelectionStart = nameBox.TextLength; };
        }
        private void Save()
        {
            try
            {
                var selected = prerequisites.CheckedItems.Cast<Choice>().Select(x => x.Id).ToList();
                if (anchor != null && before)
                {
                    int affected = Rules.CompletedAffectedByPrerequisite(store.Current, anchor).Count;
                    if (affected > 0 && ThemedDialog.Show(this, "添加未完成前置后，" + affected + " 个已完成任务需要恢复为待办。继续？", "添加前置", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
                }
                store.Change(delegate(State s)
                {
                    Todo t = id == null ? new Todo() : Rules.Get(s, id);
                    t.Name = nameBox.Text.Trim(); t.Description = descriptionBox.Text;
                    t.Due = date.Checked ? date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
                    if (!t.Done) t.Prerequisites = selected;
                    if (id == null) { if (anchor == null) s.Tasks.Add(t); else Rules.AddRelated(s, t, anchor, before); }
                });
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { ThemedDialog.Show(this, ex.Message, "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
