using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace TinyTodo
{
    // Only the two interior dividers can move. Adjacent columns share a fixed budget.
    internal sealed class ColumnWidths
    {
        internal readonly int[] Minimum;
        internal readonly int[] Width;
        internal ColumnWidths(int done, int name, int due, int state)
        { Minimum = new int[] { done, name, due, state }; Width = (int[])Minimum.Clone(); }
        internal int MinTotal { get { return Minimum.Sum(); } }
        internal void Fit(int total)
        {
            total = Math.Max(total, MinTotal);
            Width[0] = Minimum[0];
            int extra = total - MinTotal;
            // Preserve date/status preferences while giving spare space to the task name.
            Width[3] = Minimum[3] + Math.Min(Math.Max(0, Width[3] - Minimum[3]), extra);
            extra -= Width[3] - Minimum[3];
            Width[2] = Minimum[2] + Math.Min(Math.Max(0, Width[2] - Minimum[2]), extra);
            Width[1] = total - Width[0] - Width[2] - Width[3];
        }
        internal void Drag(int left, int delta)
        {
            if (left != 1 && left != 2) return;
            delta = Math.Max(Minimum[left] - Width[left], Math.Min(delta, Width[left + 1] - Minimum[left + 1]));
            Width[left] += delta; Width[left + 1] -= delta;
        }
    }

    // Entirely owner drawn: no native grid windows, cell borders or focus rectangles.
    internal sealed class TaskTable : Control, IWheelTarget, IMessageFilter
    {
        internal sealed class Row
        {
            internal Todo Task;
            internal string DateText, Status;
        }
        internal readonly List<Row> Rows = new List<Row>();
        internal readonly ColumnWidths Sizing;
        internal Action<string> OpenTask, ToggleTask;
        internal Action<string, Point> TaskMenu;
        internal Action AddTask, SortDate, ToggleDateMode;
        internal readonly SoftButton AddRowButton = new SoftButton();
        internal int Relation;
        internal readonly int ColumnHeadersHeight, RowHeight;
        internal string DateHeader = "日期";
        private readonly SoftMenu dateMenu = new SoftMenu();
        private readonly ThinScroll scroll = new ThinScroll();
        private readonly HoverMarkdown hoverPreview = new HoverMarkdown();
        private int first, selected = -1, divider = -1, lastX, wheelRemainder, downRow = -1, downColumn = -1, downClicks;
        private bool downCheck;
        private string downTaskId;
        private sealed class CompletionEffect
        {
            internal Todo Task;
            internal int Index;
            internal bool Leaving;
            internal readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();
        }
        private readonly Dictionary<string, CompletionEffect> completionEffects = new Dictionary<string, CompletionEffect>();
        private readonly Timer completionTimer = new Timer { Interval = 16 };
        private const int CompletionDuration = 520;
        private string hoverId;
        private bool countdown;
        internal int VisibleRows { get { return Math.Max(1, (ClientSize.Height - ColumnHeadersHeight) / RowHeight); } }
        internal int AvailableWidth { get { return Math.Max(1, ClientSize.Width - (scroll.Visible ? scroll.Width : 0)); } }
        internal int RequiredWidth { get { return Sizing.MinTotal + Ui.U(10); } }
        internal string SelectedId { get { return selected < 0 || selected >= Rows.Count || Rows[selected].Task == null ? null : Rows[selected].Task.Id; } }
        internal TaskTable()
        {
            Dock = DockStyle.Fill; BackColor = Theme.Canvas; ForeColor = Theme.Ink; TabStop = true;
            Font = Theme.Font(9.5F, FontStyle.Regular, "任务列表");
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardDoubleClick, true);
            ColumnHeadersHeight = Math.Max(Ui.U(36), Font.Height + Ui.U(16));
            RowHeight = Math.Max(Ui.U(40), Font.Height + Ui.U(18));
            int padding = Ui.U(22);
            Sizing = new ColumnWidths(Math.Max(Ui.U(60), Measure("完成") + padding),
                Math.Max(Ui.U(88), Measure("任务") + padding), Measure("8888-88-88") + padding,
                Math.Max(Measure("等待 99 项"), Measure("已完成")) + padding);
            completionTimer.Tick += delegate { AdvanceCompletionFeedback(); };
            scroll.BackColor = BackColor; Controls.Add(scroll);
            Application.AddMessageFilter(this);
            AddRowButton.AutoSize = false; AddRowButton.Font = Font;
            AddRowButton.BackColor = Color.White; AddRowButton.ForeColor = Theme.Ink;
            AddRowButton.Cursor = Cursors.Hand; AddRowButton.Visible = false;
            Controls.Add(AddRowButton);
            AddRowButton.Click += delegate { selected = 0; Invalidate(); if (AddTask != null) AddTask(); };
            AddRowButton.MouseWheel += delegate(object sender, MouseEventArgs e)
            { var handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true; OnMouseWheel(e); };
            scroll.Changed = value => { first = value; hoverPreview.Hide(); LayoutAddButton(); Invalidate(); };
            dateMenu.Items.Add("显示倒计时", null, delegate { if (ToggleDateMode != null) BeginInvoke(new Action(delegate { if (!IsDisposed && ToggleDateMode != null) ToggleDateMode(); })); });
        }
        internal bool HasCompletionFeedback(string id) { return id != null && completionEffects.ContainsKey(id); }
        internal void ShowCompletionFeedback(State state, string id)
        {
            int index = Rows.FindIndex(r => r.Task != null && r.Task.Id == id);
            Todo task = state.Tasks.FirstOrDefault(t => t.Id == id);
            if (index < 0 || task == null) return;
            completionEffects[id] = new CompletionEffect { Task = task, Index = index };
            completionTimer.Start();
        }
        internal void ClearCompletionFeedback()
        {
            string selectedId = SelectedId;
            Rows.RemoveAll(r => r.Task != null && completionEffects.ContainsKey(r.Task.Id) && completionEffects[r.Task.Id].Leaving);
            completionEffects.Clear(); completionTimer.Stop();
            selected = selectedId == null ? -1 : Rows.FindIndex(r => r.Task != null && r.Task.Id == selectedId);
            FitColumns();
        }
        private void AdvanceCompletionFeedback()
        {
            string selectedId = SelectedId; bool removed = false;
            foreach (var pair in completionEffects.ToArray())
            {
                if (pair.Value.Watch.ElapsedMilliseconds < CompletionDuration) continue;
                if (pair.Value.Leaving) { Rows.RemoveAll(r => r.Task != null && r.Task.Id == pair.Key); removed = true; }
                completionEffects.Remove(pair.Key);
            }
            if (removed)
            {
                selected = selectedId == null ? -1 : Rows.FindIndex(r => r.Task != null && r.Task.Id == selectedId);
                FitColumns();
            }
            if (completionEffects.Count == 0) completionTimer.Stop();
            Invalidate();
        }
        private int Measure(string text) { return TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width; }
        internal Rectangle GetColumnDisplayRectangle(int column, bool unused)
        { return new Rectangle(Sizing.Width.Take(column).Sum(), 0, Sizing.Width[column], ColumnHeadersHeight); }
        internal Rectangle CellBounds(int column, int row)
        { var r = GetColumnDisplayRectangle(column, false); if (row >= 0) { r.Y = ColumnHeadersHeight + (row - first) * RowHeight; r.Height = RowHeight; } return r; }
        internal RectangleF CheckBounds(int row)
        { Rectangle cell = CellBounds(0, row); int size = Ui.U(16); return new RectangleF(cell.X + (cell.Width - size) / 2F, cell.Y + (cell.Height - size) / 2F, size, size); }
        private void SyncScroll()
        {
            first = Math.Max(0, Math.Min(first, Math.Max(0, Rows.Count - VisibleRows)));
            scroll.SetRange(Math.Max(0, Rows.Count - VisibleRows), VisibleRows, first);
            scroll.SetBounds(ClientSize.Width - scroll.Width, ColumnHeadersHeight, scroll.Width, Math.Max(1, ClientSize.Height - ColumnHeadersHeight));
            LayoutAddButton();
        }
        private void LayoutAddButton()
        {
            if (AddRowButton == null) return;
            AddRowButton.Visible = first == 0 && Rows.Count > 0 && Rows[0].Task == null;
            float gap = Math.Max(1, RowHeight * .05F);
            // Keep the same row footprint; the button scrolls away with row zero.
            AddRowButton.Bounds = Rectangle.Round(new RectangleF(Ui.U(2), ColumnHeadersHeight + gap / 2 + Ui.U(1),
                Math.Max(1, AvailableWidth - Ui.U(4)), RowHeight - gap - Ui.U(2)));
            AddRowButton.BringToFront();
        }
        internal void FitColumns()
        {
            if (Sizing == null || scroll == null) return;
            SyncScroll();
            Form owner = FindForm();
            if (AvailableWidth <= Sizing.MinTotal + Ui.U(28) || (owner != null && owner.Width <= owner.MinimumSize.Width))
            { Sizing.Width[2] = Sizing.Minimum[2]; Sizing.Width[3] = Sizing.Minimum[3]; }
            Sizing.Fit(AvailableWidth); Invalidate();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); FitColumns(); }
        internal void ShowTasks(State state, IEnumerable<Todo> tasks, bool addRow)
        {
            countdown = state.Window.DateCountdown;
            DateHeader = (countdown ? "倒计时" : "日期") + (SortDate == null ? "" : state.Window.DateDescending ? " ↓" : " ↑");
            string id = SelectedId; hoverPreview.Hide(); hoverId = null; Rows.Clear();
            if (addRow) Rows.Add(new Row { DateText = "", Status = "" });
            AddRowButton.Text = Relation < 0 ? "＋ 添加前置" : "＋ 添加后续"; AddRowButton.AccessibleName = Relation < 0 ? "添加前置" : "添加后续";
            foreach (Todo task in tasks) Rows.Add(new Row { Task = task, DateText = Rules.DateText(task, countdown, DateTime.Today), Status = Ui.Status(state, task) });
            foreach (var pair in completionEffects.OrderBy(p => p.Value.Index).ToArray())
            {
                Todo task = state.Tasks.FirstOrDefault(t => t.Id == pair.Key);
                if (task == null || task.Done != pair.Value.Task.Done) { completionEffects.Remove(pair.Key); continue; }
                pair.Value.Task = task;
                pair.Value.Leaving = !Rows.Any(r => r.Task != null && r.Task.Id == pair.Key);
                if (pair.Value.Leaving) Rows.Insert(Math.Min(pair.Value.Index, Rows.Count), new Row
                    { Task = task, DateText = Rules.DateText(task, countdown, DateTime.Today), Status = task.Done ? "已完成" : "已恢复" });
            }
            selected = id == null ? -1 : Rows.FindIndex(r => r.Task != null && r.Task.Id == id);
            FitColumns();
        }
        private int DividerAt(Point p)
        {
            if (p.Y < 0 || p.Y >= ColumnHeadersHeight) return -1;
            for (int i = 1; i <= 2; i++) if (Math.Abs(p.X - Sizing.Width.Take(i + 1).Sum()) <= Ui.U(5)) return i;
            return -1;
        }
        private int ColumnAt(int x)
        {
            if (x < 0 || x >= AvailableWidth) return -1;
            int right = 0; for (int i = 0; i < 4; i++) { right += Sizing.Width[i]; if (x < right) return i; }
            return -1;
        }
        private int RowAt(int y)
        {
            if (y < ColumnHeadersHeight || y >= Height) return -1;
            int row = first + (y - ColumnHeadersHeight) / RowHeight;
            return row < Rows.Count ? row : -1;
        }
        private RectangleF CardBounds(int row)
        {
            float gap = Math.Max(1, RowHeight * .05F);
            return new RectangleF(Ui.U(2), ColumnHeadersHeight + (row - first) * RowHeight + gap / 2 + Ui.U(1), AvailableWidth - Ui.U(4), RowHeight - gap - Ui.U(2));
        }
        private int RowAtPoint(Point point)
        {
            int row = ColumnAt(point.X) < 0 ? -1 : RowAt(point.Y);
            return row < 0 || (Rows[row].Task != null && !CardBounds(row).Contains(point)) ? -1 : row;
        }
        private void ClearSelection()
        { if (selected < 0) return; selected = -1; Invalidate(); }
        public bool PreFilterMessage(ref Message message)
        {
            if (selected < 0 || IsDisposed || !IsHandleCreated) return false;
            int kind = message.Msg;
            bool client = kind == 0x201 || kind == 0x204 || kind == 0x207 || kind == 0x20B;
            bool caption = kind == 0xA1 || kind == 0xA4 || kind == 0xA7 || kind == 0xAB;
            if (!client && !caption) return false;
            if (!client || message.HWnd != Handle) ClearSelection();
            else
            {
                long location = message.LParam.ToInt64();
                if (RowAtPoint(new Point((short)(location & 0xffff), (short)((location >> 16) & 0xffff))) != selected) ClearSelection();
            }
            // Observe only this UI thread's clicks; never consume or redirect the input.
            return false;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus(); hoverPreview.Hide(); downRow = RowAtPoint(e.Location); downColumn = ColumnAt(e.X); downClicks = e.Clicks; downCheck = downRow >= 0 && CheckBounds(downRow).Contains(e.Location);
            downTaskId = downRow < 0 || Rows[downRow].Task == null ? null : Rows[downRow].Task.Id;
            if (downRow != selected || downColumn < 0) ClearSelection();
            if (e.Button == MouseButtons.Left && (divider = DividerAt(e.Location)) >= 0)
            { lastX = e.X; Capture = true; return; }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (divider >= 0) { Sizing.Drag(divider, e.X - lastX); lastX = e.X; Invalidate(); return; }
            Cursor = DividerAt(e.Location) >= 0 ? Cursors.VSplit : Cursors.Default;
            int row = RowAt(e.Y); Todo task = row < 0 || ColumnAt(e.X) <= 0 ? null : Rows[row].Task;
            string id = task == null ? null : task.Id;
            if (id != hoverId) { hoverId = id; if (task == null) hoverPreview.Leave(); else hoverPreview.Schedule(this, task, PointToScreen(e.Location), delegate(Point p)
                { int at = RowAt(p.Y); return at >= 0 && ColumnAt(p.X) > 0 && Rows[at].Task != null && Rows[at].Task.Id == id; }); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (divider >= 0) { divider = -1; Capture = false; return; }
            int row = RowAtPoint(e.Location);
            string upTaskId = row < 0 || Rows[row].Task == null ? null : Rows[row].Task.Id;
            if (downRow == row && downColumn == ColumnAt(e.X) && downTaskId == upTaskId) ActivateAt(e.Location, e.Button, downClicks);
        }
        private void ActivateAt(Point p, MouseButtons button, int clicks)
        {
            int column = ColumnAt(p.X); if (column < 0 || p.Y < 0) return;
            if (p.Y < ColumnHeadersHeight)
            {
                if (column != 2 || clicks != 1) return;
                if (button == MouseButtons.Left && SortDate != null) SortDate();
                if (button == MouseButtons.Right && ToggleDateMode != null)
                { dateMenu.Items[0].Text = countdown ? "显示日期" : "显示倒计时"; dateMenu.Show(this, p); }
                return;
            }
            int row = RowAtPoint(p); if (row < 0) return;
            selected = row; Invalidate(); string id = SelectedId;
            if (HasCompletionFeedback(id)) return;
            if (button == MouseButtons.Right) { if (id != null && TaskMenu != null) TaskMenu(id, p); return; }
            if (button != MouseButtons.Left) return;
            if (id == null) { if (clicks == 1 && AddTask != null) AddTask(); }
            else if (clicks == 1 && column == 0 && downCheck && CheckBounds(row).Contains(p)) { if (ToggleTask != null) ToggleTask(id); }
            else if (clicks == 2 && column == 1 && OpenTask != null) OpenTask(id);
        }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) { divider = -1; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hoverId = null; Invalidate(); hoverPreview.Leave(); base.OnMouseLeave(e); }
        bool IWheelTarget.CanScrollWheel(int delta) { return delta > 0 ? first > 0 : first < scroll.Maximum; }
        void IWheelTarget.ScrollWheel(int delta) { OnMouseWheel(new HandledMouseEventArgs(MouseButtons.None, 0, 0, 0, delta)); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true;
            hoverPreview.Hide(); int steps = WheelInput.Steps(ref wheelRemainder, e.Delta);
            int lines = SystemInformation.MouseWheelScrollLines;
            first -= steps * (lines < 0 ? VisibleRows : lines); SyncScroll(); Invalidate();
        }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || key == Keys.PageUp || key == Keys.PageDown || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Ui.ExactModifiers(e, Keys.Control) && e.KeyCode == Keys.C && SelectedId != null)
            {
                Row row = Rows[selected]; Clipboard.SetText(row.Task.Name + "\t" + row.DateText + "\t" + row.Status);
            }
            else if (Ui.ExactModifiers(e, Keys.None) && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space))
            {
                if (HasCompletionFeedback(SelectedId)) { e.SuppressKeyPress = true; return; }
                if (selected >= 0 && SelectedId == null) { if (AddTask != null) AddTask(); }
                else if (SelectedId != null && e.KeyCode == Keys.Space) { if (ToggleTask != null) ToggleTask(SelectedId); }
                else if (SelectedId != null && OpenTask != null) OpenTask(SelectedId);
            }
            else if (((Ui.ExactModifiers(e, Keys.None) && e.KeyCode == Keys.Apps) || (Ui.ExactModifiers(e, Keys.Shift) && e.KeyCode == Keys.F10)) && SelectedId != null)
            { if (TaskMenu != null) TaskMenu(SelectedId, new Point(Ui.U(40), CellBounds(1, selected).Bottom)); }
            else if (Ui.ExactModifiers(e, Keys.None) && Rows.Count > 0 && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Home || e.KeyCode == Keys.End || e.KeyCode == Keys.PageUp || e.KeyCode == Keys.PageDown))
            {
                if (e.KeyCode == Keys.Home) selected = 0;
                else if (e.KeyCode == Keys.End) selected = Rows.Count - 1;
                else selected = Math.Max(0, Math.Min(Rows.Count - 1, selected + (e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.Down ? 1 : e.KeyCode == Keys.PageUp ? -VisibleRows : VisibleRows)));
                if (selected < first) first = selected; if (selected >= first + VisibleRows) first = selected - VisibleRows + 1;
                SyncScroll(); Invalidate();
            }
            else { base.OnKeyDown(e); return; }
            e.SuppressKeyPress = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            float gap = Math.Max(1, RowHeight * .05F);
            string[] headers = { "完成", "任务", DateHeader, "状态" };
            using (var path = Theme.Rounded(new RectangleF(1, 1, Math.Max(1, AvailableWidth - 2), ColumnHeadersHeight - gap - 1), Ui.U(6)))
            using (var fill = new SolidBrush(Theme.HeaderTint)) g.FillPath(fill, path);
            for (int col = 0; col < 4; col++) DrawText(g, headers[col], CellBounds(col, -1), Theme.Ink, col == 0);
            var state = g.Save(); g.SetClip(new Rectangle(0, ColumnHeadersHeight, AvailableWidth, Math.Max(0, Height - ColumnHeadersHeight)));
            for (int i = first; i < Math.Min(Rows.Count, first + VisibleRows + 1); i++)
            {
                Row row = Rows[i]; Todo task = row.Task;
                var r = CardBounds(i);
                if (r.Width <= 0) continue;
                if (task == null) continue; // Drawn by the shared SoftButton child.
                Theme.TaskCard(g, r, task.Done, i == selected, Ui.Scale);
                CompletionEffect effect; completionEffects.TryGetValue(task.Id, out effect);
                if (effect != null)
                    using (var path = Theme.Rounded(r, Ui.U(8)))
                    using (var brush = new SolidBrush(Theme.Mix(Theme.RoseSoft, task.Done ? Theme.Done : Theme.TaskTint,
                        .5F * Math.Max(0, 1F - effect.Watch.ElapsedMilliseconds / (float)CompletionDuration)))) g.FillPath(brush, path);
                Rectangle check = CellBounds(0, i); int size = Ui.U(16);
                Theme.Check(g, new RectangleF(check.X + (check.Width - size) / 2F, check.Y + (check.Height - size) / 2F, size, size), task.Done);
                for (int col = 1; col < 4; col++)
                {
                    Rectangle cell = CellBounds(col, i);
                    if (col == 1 && task.Important)
                    { Ui.DrawImportant(g, new RectangleF(cell.X + Ui.U(8), cell.Y + (cell.Height - Ui.U(13)) / 2F, Ui.U(9), Ui.U(13))); cell.X += Ui.U(12); cell.Width -= Ui.U(12); }
                    DrawText(g, col == 1 ? task.Name : col == 2 ? row.DateText : effect != null ? (task.Done ? "已完成" : "已恢复") : row.Status, cell, task.Done ? Theme.Muted : Theme.Ink, false);
                }
                if (effect != null && effect.Leaving)
                {
                    float fade = Math.Max(0, Math.Min(1, (effect.Watch.ElapsedMilliseconds - 280) / 240F));
                    using (var brush = new SolidBrush(Color.FromArgb((int)(fade * 255), BackColor))) g.FillRectangle(brush, r);
                }
            }
            g.Restore(state);
        }
        private void DrawText(Graphics g, string text, Rectangle bounds, Color color, bool center)
        {
            bounds.Inflate(-Ui.U(8), 0);
            TextRenderer.DrawText(g, text, Theme.Font(9.5F, FontStyle.Regular, text), bounds, color,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping | (center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left));
        }
        protected override void Dispose(bool disposing) { if (disposing) { Application.RemoveMessageFilter(this); completionTimer.Dispose(); dateMenu.Dispose(); hoverPreview.Dispose(); } base.Dispose(disposing); }
    }
    internal sealed class GraphNode
    {
        internal Todo Task;
        internal RectangleF Bounds;
    }
    internal sealed class GraphLayout
    {
        internal readonly Dictionary<string, GraphNode> Nodes = new Dictionary<string, GraphNode>();
        internal readonly List<Tuple<string, string>> Edges = new List<Tuple<string, string>>();
        internal RectangleF Bounds;
        internal GraphLayout(State state, bool showDone)
        {
            var tasks = state.Tasks.Where(t => showDone || !t.Done).ToDictionary(t => t.Id);
            var degree = tasks.Values.ToDictionary(t => t.Id, t => t.Prerequisites.Count(tasks.ContainsKey));
            var children = tasks.Keys.ToDictionary(id => id, id => new List<string>());
            foreach (Todo t in tasks.Values)
                foreach (string p in t.Prerequisites.Where(tasks.ContainsKey)) { children[p].Add(t.Id); Edges.Add(Tuple.Create(p, t.Id)); }
            var depth = tasks.Keys.ToDictionary(id => id, id => 0);
            var queue = new Queue<string>(tasks.Values.Where(t => degree[t.Id] == 0).OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Select(t => t.Id));
            var order = new List<string>();
            while (queue.Count > 0)
            {
                string id = queue.Dequeue(); order.Add(id);
                foreach (string child in children[id]) { depth[child] = Math.Max(depth[child], depth[id] + 1); if (--degree[child] == 0) queue.Enqueue(child); }
            }
            foreach (var layer in order.GroupBy(id => depth[id]))
            {
                int i = 0, count = layer.Count();
                foreach (string id in layer)
                {
                    var rect = new RectangleF((i++ - (count - 1) / 2F) * 252F - 110F, layer.Key * 112F, 220F, 64F);
                    Nodes[id] = new GraphNode { Task = tasks[id], Bounds = rect };
                    Bounds = Nodes.Count == 1 ? rect : RectangleF.Union(Bounds, rect);
                }
            }
        }
    }

    internal sealed class TaskGraph : Control
    {
        internal Action<string> OpenTask, ToggleTask;
        internal Action<string, Point> TaskMenu;
        internal GraphLayout LayoutData;
        internal string CurrentId, SelectedId;
        internal float Zoom = 1F;
        internal bool FitPreview;
        internal int ToolbarBottom;
        private bool fitQueued, revealForFit;
        internal RectangleF CanvasViewport
        {
            get
            {
                Rectangle visible = ClientRectangle;
                var page = ScrollSurface.Ancestor(this);
                if (page != null && IsHandleCreated && page.IsHandleCreated)
                    visible.Intersect(RectangleToClient(page.RectangleToScreen(page.ClientRectangle)));
                int top = Math.Max(visible.Top, ToolbarBottom);
                return RectangleF.FromLTRB(visible.Left + Ui.U(16), top + Ui.U(16), Math.Max(visible.Left + Ui.U(16), visible.Right - Ui.U(16)), Math.Max(top + Ui.U(16), visible.Bottom - Ui.U(16)));
            }
        }
        internal Point CanvasCenter
        { get { var r = FitPreview ? CanvasViewport : ClientRectangle; return Point.Round(new PointF(r.Left + r.Width / 2, r.Top + r.Height / 2)); } }
        private void QueuePreviewFit(bool reveal)
        {
            if (!FitPreview || IsDisposed) return;
            revealForFit |= reveal;
            if (fitQueued || !IsHandleCreated || !Visible) return;
            fitQueued = true;
            BeginInvoke(new Action(delegate
            {
                fitQueued = false; if (IsDisposed || !Visible) return;
                var page = ScrollSurface.Ancestor(this); if (page != null) page.PerformLayout();
                bool show = revealForFit; revealForFit = false; FitPreviewCanvas(show);
            }));
        }
        private void FitPreviewCanvas(bool reveal)
        {
            if (LayoutData == null || LayoutData.Nodes.Count == 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            var page = ScrollSurface.Ancestor(this);
            if (reveal && page != null && CanvasViewport.Height < Ui.U(180))
            {
                // A long description can put the canvas below the page: reveal it on entry/reset only.
                Control section = this;
                for (Control parent = Parent; parent != null && parent != page; parent = parent.Parent)
                    if (parent is TaskViews) { section = parent; break; }
                Rectangle canvas = page.RectangleToClient(section.RectangleToScreen(section.ClientRectangle));
                page.SetPosition(page.Position + canvas.Top - Ui.U(8));
            }
            RectangleF viewport = CanvasViewport;
            if (viewport.Width <= 0 || viewport.Height <= 0) return;
            GraphNode node;
            RectangleF focus = CurrentId != null && LayoutData.Nodes.TryGetValue(CurrentId, out node) ? node.Bounds : LayoutData.Bounds;
            PointF center = new PointF(focus.Left + focus.Width / 2, focus.Top + focus.Height / 2);
            RectangleF context = focus;
            foreach (var edge in LayoutData.Edges)
            {
                string neighbor = edge.Item1 == CurrentId ? edge.Item2 : edge.Item2 == CurrentId ? edge.Item1 : null;
                if (neighbor != null) context = RectangleF.Union(context, LayoutData.Nodes[neighbor].Bounds);
            }
            float width = Math.Max(focus.Width * 2F, 2F * Math.Max(center.X - context.Left, context.Right - center.X));
            float height = Math.Max(focus.Height * 2F, 2F * Math.Max(center.Y - context.Top, context.Bottom - center.Y));
            float fit = Math.Min(viewport.Width / (width * Ui.Scale), viewport.Height / (height * Ui.Scale));
            // Keep dense neighborhoods readable; users can zoom out further when needed.
            Zoom = Math.Min(1F, Math.Max(.55F, fit));
            Zoom = Math.Min(Zoom, Math.Min(viewport.Width / (focus.Width * Ui.Scale), viewport.Height / (focus.Height * Ui.Scale)));
            Zoom = Math.Max(.2F, Zoom);
            offset = new PointF(viewport.Left + viewport.Width / 2 - center.X * Zoom * Ui.Scale, viewport.Top + viewport.Height / 2 - center.Y * Zoom * Ui.Scale);
            placed = true; Invalidate();
        }

        private PointF offset;
        private Point down, last;
        private Size previousSize;
        private bool dragging, moved, placed;
        private int downClicks;
        private string downTask;
        private bool downCheck;
        private readonly HoverMarkdown tip = new HoverMarkdown();
        private string hover;
        private bool countdown;
        internal TaskGraph()
        {
            Dock = DockStyle.Fill; BackColor = Theme.Canvas; TabStop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }
        internal void ShowTasks(State s, bool showDone, string current, bool center)
        { countdown = s.Window.DateCountdown; CurrentId = current; LayoutData = new GraphLayout(s, showDone); if (center || !placed) { if (FitPreview) QueuePreviewFit(true); else CenterCurrent(); } Invalidate(); }
        internal PointF ToScreen(PointF p) { float z = Zoom * Ui.Scale; return new PointF(p.X * z + offset.X, p.Y * z + offset.Y); }
        private PointF World(Point p) { float z = Zoom * Ui.Scale; return new PointF((p.X - offset.X) / z, (p.Y - offset.Y) / z); }
        internal void ResetView()
        { if (FitPreview) { var page = ScrollSurface.Ancestor(this); if (page != null) page.PerformLayout(); FitPreviewCanvas(true); } else { Zoom = 1F; CenterCurrent(); } }
        internal void CenterCurrent()
        {
            if (LayoutData == null || ClientSize.Width == 0 || ClientSize.Height == 0) return;
            GraphNode node; RectangleF r = CurrentId != null && LayoutData.Nodes.TryGetValue(CurrentId, out node) ? node.Bounds : LayoutData.Bounds;
            offset = new PointF(ClientSize.Width / 2F - (r.Left + r.Width / 2) * Zoom * Ui.Scale, ClientSize.Height / 2F - (r.Top + r.Height / 2) * Zoom * Ui.Scale);
            placed = true; Invalidate();
        }
        internal void ZoomAt(float factor, Point anchor)
        {
            PointF world = World(anchor); Zoom = Math.Max(.2F, Math.Min(2.5F, Zoom * factor));
            offset = new PointF(anchor.X - world.X * Zoom * Ui.Scale, anchor.Y - world.Y * Zoom * Ui.Scale); Invalidate();
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (FitPreview) QueuePreviewFit(false);
            else if (!placed) CenterCurrent();
            else { offset.X += (ClientSize.Width - previousSize.Width) / 2F; offset.Y += (ClientSize.Height - previousSize.Height) / 2F; }
            previousSize = ClientSize; Invalidate();
        }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) { if (FitPreview) QueuePreviewFit(true); else if (!placed) CenterCurrent(); } }
        private GraphNode Hit(Point p)
        { if (LayoutData == null) return null; PointF w = World(p); return LayoutData.Nodes.Values.FirstOrDefault(n => n.Bounds.Contains(w)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (LayoutData == null) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(offset.X, offset.Y); g.ScaleTransform(Zoom * Ui.Scale, Zoom * Ui.Scale);
            using (var pen = new Pen(Theme.Ink, 1.5F))
            {
                pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                foreach (var edge in LayoutData.Edges)
                {
                    RectangleF a = LayoutData.Nodes[edge.Item1].Bounds, b = LayoutData.Nodes[edge.Item2].Bounds;
                    PointF start = new PointF(a.Left + a.Width / 2, a.Bottom + 2.25F), end = new PointF(b.Left + b.Width / 2, b.Top - 2.25F);
                    using (var path = Connection(start, end)) g.DrawPath(pen, path);
                    g.DrawLines(pen, new PointF[] { new PointF(end.X - 4, end.Y - 5), end, new PointF(end.X + 4, end.Y - 5) });
                }
            }
            using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center })
            {
                foreach (GraphNode n in LayoutData.Nodes.Values)
                {
                    RectangleF r = n.Bounds;
                    Color fill = n.Task.Done ? Theme.Done : Theme.TaskTint;
                    if (n.Task.Id == SelectedId) fill = Theme.Mix(Theme.RoseSoft, fill, .25F);
                    using (var path = Theme.Rounded(r, 8F))
                    using (var brush = new SolidBrush(fill))
                    // Match the ordinary button's one-screen-pixel border at any canvas zoom.
                    using (var pen = new Pen(Theme.Border, 1F / (Zoom * Ui.Scale)))
                    { g.FillPath(brush, path); g.DrawPath(pen, path); }
                    RectangleF check = new RectangleF(r.X + 12, r.Y + 14, 14, 14);
                    Theme.Check(g, check, n.Task.Done);
                    bool current = n.Task.Id == CurrentId;
                    if (current) DrawStar(g, new PointF(r.Right - 14, r.Top + 14));
                    using (var brush = new SolidBrush(n.Task.Done ? Theme.Muted : Ui.Ink))
                    {
                        float left = r.X + 36;
                        if (n.Task.Important) { Ui.DrawImportant(g, new RectangleF(left, r.Y + 14, 9, 13)); left += 12; }
                        g.DrawString(n.Task.Name, Theme.Font(13F, FontStyle.Regular, n.Task.Name, GraphicsUnit.Pixel), brush, new RectangleF(left, r.Y + 7, r.Right - left - (current ? 28 : 12), 28), format);
                        g.DrawString(n.Task.Due == null ? "" : Rules.DateText(n.Task, countdown, DateTime.Today), Theme.Font(10F, FontStyle.Regular, "2026-09-15", GraphicsUnit.Pixel), brush, new RectangleF(r.X + 36, r.Y + 35, r.Width - 48, 18), format);
                    }
                }
            }
        }
        internal static GraphicsPath Connection(PointF start, PointF end)
        {
            var path = new GraphicsPath(); float dx = end.X - start.X;
            if (Math.Abs(dx) < 1) { path.AddLine(start, end); return path; }
            float mid = (start.Y + end.Y) / 2, direction = Math.Sign(dx);
            float r = Math.Max(0, Math.Min(8, Math.Min(Math.Abs(dx) / 2, (end.Y - start.Y) / 2)));
            path.AddLine(start, new PointF(start.X, mid - r));
            path.AddBezier(start.X, mid - r, start.X, mid, start.X, mid, start.X + direction * r, mid);
            path.AddLine(start.X + direction * r, mid, end.X - direction * r, mid);
            path.AddBezier(end.X - direction * r, mid, end.X, mid, end.X, mid, end.X, mid + r);
            path.AddLine(new PointF(end.X, mid + r), end); return path;
        }
        private static void DrawStar(Graphics g, PointF center)
        {
            var points = new PointF[10];
            for (int i = 0; i < 10; i++)
            {
                double angle = -Math.PI / 2 + i * Math.PI / 5;
                float radius = i % 2 == 0 ? 7.5F : 4F;
                points[i] = new PointF(center.X + (float)Math.Cos(angle) * radius, center.Y + (float)Math.Sin(angle) * radius);
            }
            using (var path = new GraphicsPath())
            {
                PointF previousExit = PointF.Empty;
                for (int i = 0; i < points.Length; i++)
                {
                    PointF corner = points[i], previous = points[(i + 9) % 10], next = points[(i + 1) % 10];
                    float rounding = i % 2 == 0 ? .32F : .22F;
                    var entry = new PointF(corner.X + (previous.X - corner.X) * rounding, corner.Y + (previous.Y - corner.Y) * rounding);
                    var exit = new PointF(corner.X + (next.X - corner.X) * rounding, corner.Y + (next.Y - corner.Y) * rounding);
                    if (i > 0) path.AddLine(previousExit, entry);
                    // Round both the tips and the inner valleys with tangent curves.
                    path.AddBezier(entry,
                        new PointF(entry.X + (corner.X - entry.X) * 2F / 3F, entry.Y + (corner.Y - entry.Y) * 2F / 3F),
                        new PointF(exit.X + (corner.X - exit.X) * 2F / 3F, exit.Y + (corner.Y - exit.Y) * 2F / 3F), exit);
                    previousExit = exit;
                }
                path.CloseFigure();
                using (var brush = new SolidBrush(Color.FromArgb(212, 155, 65)))
                using (var pen = new Pen(Color.FromArgb(212, 155, 65), 1.3F) { LineJoin = LineJoin.Round })
                { g.FillPath(brush, path); g.DrawPath(pen, path); }
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            tip.Hide(); base.OnMouseDown(e); Focus();
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
            {
                GraphNode n = Hit(e.Location);
                downTask = n == null ? null : n.Task.Id; downCheck = n != null && CheckHit(n, e.Location);
                downClicks = e.Clicks; dragging = true; moved = false; down = last = e.Location; Capture = true;
            }
            if (e.Button == MouseButtons.Right) { GraphNode n = Hit(e.Location); if (n != null) { SelectedId = n.Task.Id; Invalidate(); if (TaskMenu != null) TaskMenu(n.Task.Id, e.Location); } }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                if (Math.Abs(e.X - down.X) + Math.Abs(e.Y - down.Y) > Ui.U(4)) moved = true;
                if (moved) { offset.X += e.X - last.X; offset.Y += e.Y - last.Y; Invalidate(); }
                last = e.Location; Cursor = Cursors.SizeAll; return;
            }
            GraphNode n = Hit(e.Location); Cursor = n == null ? Cursors.Hand : Cursors.Default;
            string id = n == null ? null : n.Task.Id;
            if (hover != id) { hover = id; if (n == null) tip.Leave(); else tip.Schedule(this, n.Task, PointToScreen(e.Location), delegate(Point p)
                { GraphNode target = Hit(p); return target != null && target.Task.Id == id; }); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); bool click = dragging && !moved && e.Button == MouseButtons.Left;
            dragging = false; Capture = false; Cursor = Cursors.Default;
            if (!click) return; GraphNode n = Hit(e.Location); if (n == null || n.Task.Id != downTask) return;
            SelectedId = n.Task.Id; Invalidate();
            bool check = CheckHit(n, e.Location);
            if (downClicks == 1 && downCheck && check) { if (ToggleTask != null) ToggleTask(n.Task.Id); }
            else if (downClicks == 2 && !downCheck && !check && OpenTask != null) OpenTask(n.Task.Id);
        }
        private bool CheckHit(GraphNode node, Point point)
        { return new RectangleF(node.Bounds.X + 12, node.Bounds.Y + 14, 14, 14).Contains(World(point)); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragging = false; }
        internal void ZoomWheel(int delta, Point anchor)
        {
            if (delta == 0) return;
            tip.Hide();
            // A wheel gesture must not become a checkbox click or double-click on release.
            if (dragging) { moved = true; last = anchor; }
            ZoomAt((float)Math.Pow(1.15, delta / 120.0), anchor);
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true;
            var page = ScrollSurface.Ancestor(this);
            if (page != null) page.RouteWheel(this, e.Delta);
            else ZoomWheel(e.Delta, e.Location);
        }
        protected override bool IsInputKey(Keys keyData)
        { Keys key = keyData & Keys.KeyCode; return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Ui.ExactModifiers(e, Keys.Control) && e.KeyCode == Keys.C && SelectedId != null && LayoutData != null && LayoutData.Nodes.ContainsKey(SelectedId)) Clipboard.SetText(LayoutData.Nodes[SelectedId].Task.Name);
            else if (Ui.ExactModifiers(e, Keys.None) && e.KeyCode == Keys.Home) ResetView();
            else if (Ui.ExactModifiers(e, Keys.None) && e.KeyCode == Keys.Left) offset.X += Ui.U(40);
            else if (Ui.ExactModifiers(e, Keys.None) && e.KeyCode == Keys.Right) offset.X -= Ui.U(40);
            else if (Ui.ExactModifiers(e, Keys.None) && e.KeyCode == Keys.Up) offset.Y += Ui.U(40);
            else if (Ui.ExactModifiers(e, Keys.None) && e.KeyCode == Keys.Down) offset.Y -= Ui.U(40);
            else if ((Ui.ExactModifiers(e, Keys.None) || Ui.ExactModifiers(e, Keys.Shift)) && (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus)) ZoomAt(1.15F, new Point(Width / 2, Height / 2));
            else if (Ui.ExactModifiers(e, Keys.None) && (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)) ZoomAt(1 / 1.15F, new Point(Width / 2, Height / 2));
            else return;
            e.SuppressKeyPress = true; Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e) { hover = null; tip.Leave(); base.OnMouseLeave(e); }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class EyeButton : IconButton
    {
        internal bool IsOpen { get { return Open; } set { Open = value; Invalidate(); } }
        internal EyeButton() : base(Glyph.Eye) { AccessibleName = "显示已完成任务"; }
    }
    internal sealed class TrackButton : IconButton
    { internal TrackButton() : base(Glyph.Track) { } }

    // Shared navigation and graph renderer for main, history and previews.
    internal sealed class TaskViews : UserControl
    {
        internal readonly TaskTable First, Second;
        internal readonly TaskGraph Graph;
        internal readonly EyeButton Eye;
        internal Action ChangedView;
        internal Action BeforeTree;
        private readonly Panel body;
        private readonly TableLayoutPanel layout;
        private readonly Panel bar;
        private readonly FlowLayoutPanel navigation, tools;
        private bool detached, arrangingBar;
        internal Control DetachNavigation()
        { detached = true; bar.Controls.Remove(navigation); UpdateBar(); return navigation; }
        private void UpdateBar()
        {
            tools.Visible = Mode == 2; bar.Visible = !detached;
            ArrangeBar();
        }
        private static Size StripSize(FlowLayoutPanel strip)
        {
            int width = strip.Padding.Horizontal, height = 0;
            foreach (Control child in strip.Controls)
            {
                Size size = child.GetPreferredSize(Size.Empty);
                width += size.Width + child.Margin.Horizontal;
                height = Math.Max(height, size.Height + child.Margin.Vertical);
            }
            return new Size(width, height + strip.Padding.Vertical);
        }
        private void ArrangeBar()
        {
            if (arrangingBar) return;
            arrangingBar = true;
            try
            {
                Size nav = StripSize(navigation), tool = StripSize(tools);
                int height = detached ? 0 : nav.Height + (preview ? Ui.ViewTabGap : 0);
                if (!detached) navigation.SetBounds(0, 0, nav.Width, nav.Height);
                if (Graph != null) { tools.SetBounds(Math.Max(0, Graph.ClientSize.Width - tool.Width - Ui.U(8)), Ui.U(8), tool.Width, tool.Height); Graph.ToolbarBottom = tools.Bottom; }
                tools.BringToFront();
                layout.RowStyles[0].SizeType = SizeType.Absolute;
                if (layout.RowStyles[0].Height != height) layout.RowStyles[0].Height = height;
            }
            finally { arrangingBar = false; }
        }
        private readonly Button firstButton, secondButton, treeButton, track, minus, plus;
        private readonly ToolTip tips = new ToolTip();
        private readonly bool preview;
        private State state;
        private string current;
        internal int Mode { get; private set; }
        internal int PageContentHeight
        {
            get
            {
                TaskTable table = Mode == 1 && Second != null ? Second : First;
                int bodyHeight = Mode == 2 ? Ui.U(420) : Math.Max(Ui.U(190), table.ColumnHeadersHeight + table.Rows.Count * table.RowHeight + Ui.U(12));
                return bodyHeight + StripSize(navigation).Height + Ui.ViewTabGap + Ui.U(12);
            }
        }
        internal bool ShowDone { get { return Eye.IsOpen; } }
        internal TaskViews(bool preview)
        {
            this.preview = preview; Dock = DockStyle.Fill;
            var root = layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowStyles.Add(Ui.Auto()); root.RowStyles.Add(Ui.Fill());
            bar = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            // Explicit strip bounds prevent docked AutoSize panels wrapping tabs into a clipped row.
            navigation = Ui.Bar(); navigation.Dock = DockStyle.None; navigation.AutoSize = false; navigation.WrapContents = false;
            tools = Ui.Bar(); tools.Dock = DockStyle.None; tools.AutoSize = false; tools.WrapContents = false;
            bar.Controls.Add(navigation);
            firstButton = Ui.Button(preview ? "前置" : "列表", delegate { SelectView(0); }); navigation.Controls.Add(firstButton);
            if (preview) { secondButton = Ui.Button("后续", delegate { SelectView(1); }); navigation.Controls.Add(secondButton); }
            treeButton = Ui.Button("树形", delegate { SelectView(2); }); navigation.Controls.Add(treeButton);
            ((SoftButton)firstButton).IndexTab = true; ((SoftButton)treeButton).IndexTab = true;
            if (secondButton != null) ((SoftButton)secondButton).IndexTab = true;
            foreach (Control tab in navigation.Controls) tab.Margin = new Padding(Ui.U(1), preview ? 0 : Ui.U(3), Ui.U(1), 0);
            Eye = new EyeButton { Margin = new Padding(Ui.U(3)) };
            Eye.Click += delegate { SetShowDone(!Eye.IsOpen); };
            tools.Controls.Add(Eye);
            minus = new IconButton(Glyph.Minus); minus.Click += delegate { Graph.ZoomAt(1 / 1.15F, Graph.CanvasCenter); };
            plus = new IconButton(Glyph.Plus); plus.Click += delegate { Graph.ZoomAt(1.15F, Graph.CanvasCenter); };
            track = new TrackButton(); track.Click += delegate { LocateCurrent(); };
            tips.SetToolTip(minus, "缩小"); tips.SetToolTip(plus, "放大"); tips.SetToolTip(track, preview ? "归位到当前任务" : "归位");
            track.AccessibleName = preview ? "归位到当前任务" : "归位";
            tools.Controls.AddRange(new Control[] { minus, plus, track });
            body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Theme.Canvas, Padding = Padding.Empty };
            First = new TaskTable { Relation = preview ? -1 : 0 }; body.Controls.Add(First);
            if (preview) { Second = new TaskTable { Relation = 1 }; body.Controls.Add(Second); }
            Graph = new TaskGraph { FitPreview = preview }; body.Controls.Add(Graph); Graph.Controls.Add(tools); tools.BackColor = Theme.Canvas; Graph.Resize += delegate { ArrangeBar(); };
            bar.Layout += delegate { ArrangeBar(); };
            root.Controls.Add(bar, 0, 0); root.Controls.Add(body, 0, 1); Controls.Add(root); SelectView(0);
        }
        internal bool HasCompletionFeedback(string id)
        { return First.HasCompletionFeedback(id) || (Second != null && Second.HasCompletionFeedback(id)); }
        internal void ShowCompletionFeedback(State state, string id)
        { First.ShowCompletionFeedback(state, id); if (Second != null) Second.ShowCompletionFeedback(state, id); }
        internal void ClearCompletionFeedback()
        { First.ClearCompletionFeedback(); if (Second != null) Second.ClearCompletionFeedback(); }
        internal void SelectView(int mode)
        {
            if (mode == 2 && Mode != 2 && BeforeTree != null) BeforeTree();
            Mode = mode; First.Visible = mode == 0; if (Second != null) Second.Visible = mode == 1; Graph.Visible = mode == 2;
            Eye.Visible = minus.Visible = plus.Visible = track.Visible = mode == 2; UpdateBar();
            ((SoftButton)firstButton).SelectedTab = mode == 0; firstButton.Invalidate();
            if (secondButton != null) { ((SoftButton)secondButton).SelectedTab = mode == 1; secondButton.Invalidate(); }
            ((SoftButton)treeButton).SelectedTab = mode == 2; treeButton.Invalidate();
            if (mode == 2) { if (!preview) Graph.CenterCurrent(); Graph.Focus(); if (preview) Graph.ResetView(); }
            if (ChangedView != null) ChangedView();
        }
        internal void SetShowDone(bool show)
        {
            Eye.IsOpen = show; Eye.Invalidate(); tips.SetToolTip(Eye, show ? "隐藏已完成任务" : "显示已完成任务");
            Eye.AccessibleName = show ? "隐藏已完成任务" : "显示已完成任务";
            if (state != null) Graph.ShowTasks(state, show, current, true);
        }
        internal void LocateCurrent()
        { if (state != null && current != null && Rules.Get(state, current).Done) SetShowDone(true); Graph.Focus(); Graph.ResetView(); }
        internal void Render(State s, string currentId, IEnumerable<Todo> rows)
        {
            bool changed = current != currentId;
            if (changed) ClearCompletionFeedback();
            state = s; current = currentId;
            if (preview)
            {
                Todo t = Rules.Get(s, current); First.ShowTasks(s, Rules.ByDate(t.Prerequisites.Select(id => Rules.Get(s, id)), s.Window.DateDescending), true);
                Second.ShowTasks(s, Rules.ByDate(s.Tasks.Where(x => x.Prerequisites.Contains(current)), s.Window.DateDescending), true);
                if (changed && t.Done) SetShowDone(true);
            }
            else First.ShowTasks(s, rows, false);
            Graph.ShowTasks(s, ShowDone, current, changed);
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }
}
