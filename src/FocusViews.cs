using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace TinyTodo
{
    internal static class CountdownBadge
    {
        internal static Size Size { get { return new Size(Ui.U(112), Ui.U(30)); } }
        internal static string Text(int seconds)
        { return (seconds / 3600).ToString("00") + ":" + (seconds % 3600 / 60).ToString("00") + ":" + (seconds % 60).ToString("00"); }
        internal static void Draw(Graphics g, Rectangle bounds, int remaining, int duration, bool paused)
        {
            var state = g.Save(); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float stroke = Ui.Scale;
            var outline = new RectangleF(bounds.X + stroke, bounds.Y + stroke, bounds.Width - 2 * stroke, bounds.Height - 2 * stroke);
            using (var path = Theme.Rounded(outline, outline.Height / 2))
            using (var fill = new SolidBrush(Theme.Canvas))
            using (var pen = new Pen(Theme.Border, stroke))
            { g.FillPath(fill, path); g.DrawPath(pen, path); }
            int diameter = Ui.U(20);
            var dial = new Rectangle(bounds.Left + Ui.U(6), bounds.Top + (bounds.Height - diameter) / 2, diameter, diameter);
            using (var fill = new SolidBrush(Theme.Done)) g.FillEllipse(fill, dial);
            float sweep = duration == 0 ? 0 : 360F * remaining / duration;
            using (var fill = new SolidBrush(paused ? Theme.PaletteTaupe : Theme.AddAction))
            { if (sweep >= 360) g.FillEllipse(fill, dial); else if (sweep > 0) g.FillPie(fill, dial, -90, sweep); }
            using (var pen = new Pen(Theme.Latte, stroke)) g.DrawEllipse(pen, dial);
            if (paused)
                using (var pen = new Pen(Theme.Ink, Ui.U(2)))
                {
                    int center = dial.Left + dial.Width / 2;
                    g.DrawLine(pen, center - Ui.U(3), dial.Top + Ui.U(6), center - Ui.U(3), dial.Bottom - Ui.U(6));
                    g.DrawLine(pen, center + Ui.U(3), dial.Top + Ui.U(6), center + Ui.U(3), dial.Bottom - Ui.U(6));
                }
            var text = new Rectangle(dial.Right + Ui.U(5), bounds.Top, bounds.Right - dial.Right - Ui.U(12), bounds.Height);
            TextRenderer.DrawText(g, Text(remaining), Theme.Font(10, FontStyle.Regular, "00:00:00"), text, Theme.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            g.Restore(state);
        }
    }
    internal sealed class FocusBanner : Control
    {
        private string taskName;
        private int remaining, duration;
        private bool paused;
        private readonly ToolTip tip = new ToolTip();
        internal FocusBanner()
        {
            Height = Ui.U(42); Dock = DockStyle.Fill; Margin = new Padding(Ui.U(3), 0, Ui.U(3), Ui.U(6));
            DoubleBuffered = true; BackColor = Theme.Canvas; Cursor = Cursors.Hand; Visible = false;
            AccessibleName = "当前专注任务"; AccessibleRole = AccessibleRole.PushButton; TabStop = true;
        }
        internal void Update(Todo task, int seconds, int total, bool isPaused)
        {
            string name = task == null ? null : task.Name;
            if (taskName == name && remaining == seconds && duration == total && paused == isPaused) return;
            if (taskName != name) tip.SetToolTip(this, name);
            taskName = name; remaining = seconds; duration = total; paused = isPaused; Visible = task != null;
            AccessibleDescription = task == null ? null : (paused ? "已暂停 · " : "正在专注 · ") + name + " · " + CountdownBadge.Text(seconds);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (taskName == null) return;
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), Ui.U(9)))
            using (var fill = new SolidBrush(Theme.TaskTint))
            using (var pen = new Pen(Focused ? Theme.Latte : Theme.Border, Ui.Scale))
            { g.FillPath(fill, path); g.DrawPath(pen, path); }
            var badge = new Rectangle(Width - CountdownBadge.Size.Width - Ui.U(7), (Height - CountdownBadge.Size.Height) / 2, CountdownBadge.Size.Width, CountdownBadge.Size.Height);
            CountdownBadge.Draw(g, badge, remaining, duration, paused);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, paused ? "已暂停" : "正在专注", Theme.Font(9, FontStyle.Regular, "正在专注"), new Rectangle(Ui.U(12), 0, Ui.U(56), Height), Theme.Muted, flags);
            TextRenderer.DrawText(g, taskName, Theme.Font(10.5F, FontStyle.Bold, taskName), new Rectangle(Ui.U(70), 0, Math.Max(0, badge.Left - Ui.U(80)), Height), Theme.Ink, flags);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        { base.OnKeyDown(e); if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = e.SuppressKeyPress = true; OnClick(EventArgs.Empty); } }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class RepeatPicker : FlowLayoutPanel
    {
        internal readonly ZoneSelector Kind = new ZoneSelector { AccessibleName = "循环频率" };
        internal readonly ZoneSelector Day = new ZoneSelector { AccessibleName = "循环日期" };
        internal RepeatPicker(RepeatRule rule)
        {
            AutoSize = true; Dock = DockStyle.Top; WrapContents = false;
            Controls.Add(Ui.Label("循环")); Controls.Add(Kind); Controls.Add(Day); Ui.CenterRow(this);
            Kind.Items.AddRange(new object[] { "不循环", "每天", "每周", "每月" });
            Kind.SelectedItem = Kind.Items[rule == null ? 0 : (int)rule.Kind];
            Kind.SelectionChangeCommitted += delegate { SetDays(0); };
            SetDays(rule == null ? 0 : rule.Day);
        }
        private void SetDays(int day)
        {
            int kind = Kind.Items.IndexOf(Kind.SelectedItem); Day.Items.Clear();
            Day.Visible = kind == 2 || kind == 3;
            if (kind == 2) Day.Items.AddRange(new object[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" });
            if (kind == 3) for (int i = 1; i <= 31; i++) Day.Items.Add("第 " + i + " 天");
            if (kind == 2 || kind == 3) Day.SelectedItem = Day.Items[kind == 2 ? Math.Min(day, 6) : Math.Max(0, day - 1)];
        }
        internal RepeatRule Rule
        {
            get { var kind = (RepeatKind)Kind.Items.IndexOf(Kind.SelectedItem); return kind == RepeatKind.None ? null : new RepeatRule { Kind = kind, Day = Day.Items.IndexOf(Day.SelectedItem) + (kind == RepeatKind.Monthly ? 1 : 0) }; }
        }
    }
    internal sealed class FocusPanel : Panel
    {
        private readonly Store store;
        private string taskId;
        private readonly Label total = Ui.Label("");
        internal readonly TextBox Minutes = new TextBox { Text = "25", Width = Ui.U(48), MaxLength = 4, AccessibleName = "专注分钟数" };
        internal readonly Button Start, Stop;
        private readonly Label remaining = Ui.Label("");
        private readonly FocusChart chart;
        private readonly Timer timer = new Timer { Interval = 500 };
        internal FocusPanel(Store store)
        {
            this.store = store; Height = Ui.U(280); BackColor = Theme.Canvas;
            total.Font = Theme.Font(11, FontStyle.Bold, "累计专注");
            var actions = Ui.Bar(); actions.Dock = DockStyle.Top;
            var input = new InputSurface(Minutes) { Width = Ui.U(64) };
            Minutes.Dock = DockStyle.None;
            input.Layout += delegate { Minutes.SetBounds(input.Padding.Left, (input.Height - Minutes.PreferredHeight) / 2, input.Width - input.Padding.Horizontal, Minutes.PreferredHeight); };
            actions.Controls.Add(input); actions.Controls.Add(Ui.Label("分钟"));
            Start = Ui.Button("开始专注", delegate { Run(delegate {
                int minutes;
                if (!Int32.TryParse(Minutes.Text, out minutes)) throw new InvalidOperationException("请输入整数分钟。");
                if (store.Focus.TaskId == taskId) { if (store.Focus.Paused) store.Focus.Resume(); else store.Focus.Pause(); }
                else store.Focus.Start(taskId, minutes);
            }); });
            Stop = Ui.Button("结束", delegate { Run(delegate { store.Focus.Stop(); }); });
            actions.Controls.Add(Start); actions.Controls.Add(Stop); actions.Controls.Add(remaining); Ui.CenterRow(actions);
            chart = new FocusChart(store); chart.Dock = DockStyle.Fill;
            var root = Ui.Root(Ui.Auto(), Ui.Auto(), Ui.Fill());
            root.Controls.Add(total, 0, 0); root.Controls.Add(actions, 0, 1); root.Controls.Add(chart, 0, 2); Controls.Add(root);
            timer.Tick += delegate { RefreshStats(); }; timer.Start();
        }
        internal void Bind(string id) { taskId = id; chart.TaskId = id; RefreshStats(); }
        private void Run(Action action)
        {
            try { action(); RefreshStats(); }
            catch (InvalidOperationException ex) { ThemedDialog.Notify(FindForm(), ex.Message, "专注时钟"); }
            catch (System.IO.IOException ex) { ThemedDialog.Notify(FindForm(), ex.Message, "专注记录保存失败"); }
            catch (UnauthorizedAccessException ex) { ThemedDialog.Notify(FindForm(), ex.Message, "专注记录保存失败"); }
        }
        private void RefreshStats()
        {
            var task = store.Current.Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) { Start.Enabled = Stop.Enabled = false; return; }
            long seconds = Productivity.FocusSeconds(store.Current, task);
            total.Text = "累计专注  " + (seconds / 3600) + " 小时 " + (seconds % 3600 / 60) + " 分钟";
            bool active = store.Focus.TaskId != null, own = store.Focus.TaskId == taskId;
            Start.Enabled = !task.Done && (!active || own); Stop.Enabled = own; Minutes.Enabled = !own;
            Start.Text = own ? (store.Focus.Paused ? "继续" : "暂停") : "开始专注";
            remaining.Text = own ? CountdownBadge.Text(store.Focus.RemainingSeconds) : active ? "其他任务正在专注" : "";
            chart.Invalidate();
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class FocusChart : Panel
    {
        private readonly Store store;
        internal string TaskId;
        private readonly ZoneSelector period = new ZoneSelector { AccessibleName = "专注统计周期" };
        private int offset;
        private readonly ToolTip tip = new ToolTip();
        private PointF[] points = new PointF[0];
        private DateTime[] dates = new DateTime[0];
        private long[] seconds = new long[0];
        internal FocusChart(Store store)
        {
            this.store = store; DoubleBuffered = true; BackColor = Color.White;
            var bar = Ui.Bar(); bar.Dock = DockStyle.Top;
            period.Items.AddRange(new object[] { "周", "月", "年" }); period.SelectedItem = period.Items[0];
            period.SelectionChangeCommitted += delegate { offset = 0; Invalidate(); };
            var previous = new IconButton(Glyph.Left) { AccessibleName = "上一周期" }; previous.Click += delegate { offset--; Invalidate(); };
            var next = new IconButton(Glyph.Right) { AccessibleName = "下一周期" }; next.Click += delegate { if (offset < 0) offset++; Invalidate(); };
            bar.Controls.Add(Ui.Label("每日专注")); bar.Controls.Add(previous); bar.Controls.Add(period); bar.Controls.Add(next); Ui.CenterRow(bar); Controls.Add(bar);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var task = store.Current.Tasks.FirstOrDefault(t => t.Id == TaskId); if (task == null) return;
            int mode = period.Items.IndexOf(period.SelectedItem); DateTime start = DateTime.Today;
            if (mode == 0) start = start.AddDays(-(((int)start.DayOfWeek + 6) % 7) + 7 * offset);
            else if (mode == 1) start = new DateTime(start.Year, start.Month, 1).AddMonths(offset);
            else start = new DateTime(start.Year + offset, 1, 1);
            int count = mode == 0 ? 7 : mode == 1 ? DateTime.DaysInMonth(start.Year, start.Month) : DateTime.IsLeapYear(start.Year) ? 366 : 365;
            var daily = Productivity.DailyFocus(store.Current, task);
            dates = Enumerable.Range(0, count).Select(i => start.AddDays(i)).ToArray();
            seconds = dates.Select(d => { long value; return daily.TryGetValue(d.ToString("yyyy-MM-dd"), out value) ? value : 0; }).ToArray();
            long max = Math.Max(60, seconds.Max());
            var area = new RectangleF(Ui.U(38), Controls[0].Bottom + Ui.U(8), Math.Max(1, Width - Ui.U(60)), Math.Max(1, Height - Controls[0].Bottom - Ui.U(36)));
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var grid = new Pen(Theme.Border)) { g.DrawLine(grid, area.Left, area.Bottom, area.Right, area.Bottom); g.DrawLine(grid, area.Left, area.Top, area.Left, area.Bottom); }
            using (var brush = new SolidBrush(Theme.Muted))
            {
                var tickFlags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
                int tickWidth = (int)area.Left - Ui.U(5), tickHeight = Font.Height + Ui.U(4);
                TextRenderer.DrawText(g, (max / 60) + "分", Font, new Rectangle(0, (int)area.Top - tickHeight / 2, tickWidth, tickHeight), Theme.Muted, tickFlags);
                TextRenderer.DrawText(g, "0", Font, new Rectangle(0, (int)area.Bottom - tickHeight / 2, tickWidth, tickHeight), Theme.Muted, tickFlags);
                string label = start.ToString("yyyy-MM-dd") + " — " + dates.Last().ToString("yyyy-MM-dd");
                g.DrawString(label, Font, brush, area.Left, area.Bottom + Ui.U(3));
            }
            points = seconds.Select((s, i) => new PointF(area.Left + area.Width * i / (count - 1), area.Bottom - area.Height * s / max)).ToArray();
            int elapsedDays = dates.Count(d => d <= DateTime.Today);
            if (elapsedDays > 1) using (var pen = new Pen(Theme.Rose, Ui.U(1))) g.DrawLines(pen, points.Take(elapsedDays).ToArray());
            using (var brush = new SolidBrush(Theme.Rose)) for (int i = 0; i < elapsedDays; i++) if (mode != 2 || seconds[i] > 0) g.FillEllipse(brush, points[i].X - Ui.U(2), points[i].Y - Ui.U(2), Ui.U(4), Ui.U(4));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (points.Length == 0) return;
            int i = Enumerable.Range(0, points.Length).OrderBy(n => Math.Abs(points[n].X - e.X)).First();
            if (dates[i] > DateTime.Today) { tip.SetToolTip(this, null); return; }
            tip.SetToolTip(this, dates[i].ToString("yyyy-MM-dd") + " · " + (seconds[i] / 3600) + " 小时 " + (seconds[i] % 3600 / 60) + " 分钟");
        }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class FocusForm : AppWindow
    {
        internal readonly string TaskId;
        internal FocusForm(Store store, string id)
        {
            TaskId = id; Ui.Setup(this, "专注 · " + Rules.Get(store.Current, id).Name, 580, 380);
            var panel = new FocusPanel(store) { Dock = DockStyle.Fill }; Controls.Add(panel); panel.Bind(id);
            TaskWindows.Watch(store, this, delegate { if (!store.Current.Tasks.Any(t => t.Id == id)) Close(); });
        }
    }
}
