using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TinyTodo
{
    internal sealed class ThinScroll : Control
    {
        internal int Maximum, Page = 1, Value;
        internal Action<int> Changed;
        private bool dragging; private int anchor, origin;
        internal ThinScroll() { DoubleBuffered = true; Width = Ui.U(10); Cursor = Cursors.Hand; TabStop = false; BackColor = Color.White; }
        internal void SetRange(int maximum, int page, int value)
        { Maximum = Math.Max(0, maximum); Page = Math.Max(1, page); Value = Math.Max(0, Math.Min(Maximum, value)); Visible = Maximum > 0; Invalidate(); }
        private Rectangle Thumb
        {
            get { int h = Math.Min(Height, Math.Max(Ui.U(25), Height * Page / Math.Max(1, Maximum + Page)));
                return new Rectangle(Ui.U(2), Maximum == 0 ? 0 : (Height - h) * Value / Maximum, Math.Max(2, Width - Ui.U(4)), h); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (Maximum <= 0 || Height < 2) return;
            using (var path = Theme.Rounded(Thumb, Ui.U(3))) using (var b = new SolidBrush(dragging ? Theme.Quote : Theme.Border)) e.Graphics.FillPath(b, path);
        }
        private void MoveTo(int value) { Value = Math.Max(0, Math.Min(Maximum, value)); Invalidate(); if (Changed != null) Changed(Value); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            if (Thumb.Contains(e.Location)) { dragging = true; anchor = e.Y; origin = Value; Capture = true; }
            else MoveTo(Value + (e.Y < Thumb.Top ? -Page : Page));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        { base.OnMouseMove(e); if (dragging) MoveTo(origin + (e.Y - anchor) * Maximum / Math.Max(1, Height - Thumb.Height)); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) dragging = false; base.OnMouseCaptureChanged(e); }
    }
    internal sealed class TextViewport : SurfacePanel, IMessageFilter
    {
        internal readonly TextBoxBase Editor;
        internal readonly ThinScroll Scrollbar = new ThinScroll();
        private readonly Timer timer = new Timer { Interval = 160 };
        private int documentHeight, wheelRemainder;
        private bool measuring, needsMeasure = true;
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr Send(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendPoint(IntPtr h, int msg, IntPtr w, ref Point point);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        internal TextViewport(TextBoxBase editor)
        {
            Editor = editor; Dock = DockStyle.Fill; Padding = new Padding(Ui.U(6));
            editor.BorderStyle = BorderStyle.None; editor.ForeColor = Theme.Ink; editor.BackColor = Color.White; editor.Dock = DockStyle.None;
            var rich = editor as RichTextBox;
            if (rich != null)
            {
                rich.ScrollBars = RichTextBoxScrollBars.None;
                rich.ContentsResized += delegate(object sender, ContentsResizedEventArgs e) { documentHeight = e.NewRectangle.Height; };
            }
            var plain = editor as TextBox; if (plain != null) plain.ScrollBars = ScrollBars.None;
            Controls.Add(editor); Controls.Add(Scrollbar); Scrollbar.Changed = SetPosition;
            timer.Tick += delegate { Sync(); }; timer.Start();
            editor.TextChanged += delegate { needsMeasure = true; };
            editor.FontChanged += delegate { needsMeasure = true; };
            editor.Resize += delegate { needsMeasure = true; };
            editor.HandleCreated += delegate { needsMeasure = true; };
            Application.AddMessageFilter(this);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (Editor == null || Scrollbar == null) return;
            // Reserve a stable gutter even when the thumb is hidden: text never reflows on scroll.
            Rectangle area = DisplayRectangle;
            Scrollbar.SetBounds(area.Right - Scrollbar.Width, area.Top, Scrollbar.Width, Math.Max(1, area.Height));
            Editor.SetBounds(area.Left, area.Top, Math.Max(1, area.Width - Scrollbar.Width), Math.Max(1, area.Height));
        }
        internal int Position
        {
            get
            {
                if (!Editor.IsHandleCreated) return 0;
                if (Editor is RichTextBox) { Point point = Point.Empty; SendPoint(Editor.Handle, 0x4DD, IntPtr.Zero, ref point); return point.Y; }
                return (int)Send(Editor.Handle, 0xCE, IntPtr.Zero, IntPtr.Zero);
            }
        }
        internal void SetPosition(int value)
        {
            if (!Editor.IsHandleCreated) return;
            value = Math.Max(0, Math.Min(Scrollbar.Maximum, value));
            if (Editor is RichTextBox) { Point point = new Point(0, value); SendPoint(Editor.Handle, 0x4DE, IntPtr.Zero, ref point); }
            else Send(Editor.Handle, 0xB6, IntPtr.Zero, new IntPtr(value - Position));
            Sync();
        }
        internal void ScrollWheel(int delta)
        {
            Sync(); wheelRemainder += delta; int steps = wheelRemainder / 120; wheelRemainder %= 120;
            int lines = SystemInformation.MouseWheelScrollLines;
            int amount = lines < 0 ? Scrollbar.Page : lines * (Editor is RichTextBox ? Editor.Font.Height : 1);
            if (steps != 0) SetPosition(Position - steps * amount);
        }
        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg != 0x20A || !Visible || IsDisposed) return false;
            Control hit = Control.FromChildHandle(WindowFromPoint(Cursor.Position));
            if (hit != this && (hit == null || !Contains(hit))) return false;
            ScrollWheel((short)((message.WParam.ToInt64() >> 16) & 0xffff)); return true;
        }
        internal void Sync()
        {
            if (!Visible || !Editor.IsHandleCreated || measuring) return;
            measuring = true;
            try
            {
                int page, total;
                if (Editor is RichTextBox)
                {
                    if (needsMeasure) { needsMeasure = false; Send(Editor.Handle, 0x441, IntPtr.Zero, IntPtr.Zero); }
                    page = Math.Max(1, Editor.ClientSize.Height);
                    total = Math.Max(0, documentHeight);
                }
                else
                {
                    page = Math.Max(1, Editor.ClientSize.Height / Math.Max(1, Editor.Font.Height));
                    total = (int)Send(Editor.Handle, 0xBA, IntPtr.Zero, IntPtr.Zero);
                }
                Scrollbar.SetRange(Math.Max(0, total - page), page, Position);
            }
            finally { measuring = false; }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { Application.RemoveMessageFilter(this); timer.Dispose(); } base.Dispose(disposing); }
    }
    internal sealed class PreviewLayout : Panel
    {
        private Control actions, left, delete, title, meta, description, counts, views;
        private bool arranging;
        internal void Bind(Control actions, Control left, Control delete, Control title, Control meta, MarkdownView markdown, Control counts, Control views)
        {
            this.actions = actions; this.left = left; this.delete = delete; this.title = title; this.meta = meta;
            this.description = new TextViewport(markdown); this.counts = counts; this.views = views;
            foreach (var c in new Control[] { actions, title, meta, description, counts, views }) { c.Dock = DockStyle.None; Controls.Add(c); }
            left.Dock = DockStyle.None; left.AutoSize = false; delete.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            PerformLayout();
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (views == null || arranging) return; arranging = true;
            try
            {
                int pad = Ui.U(10), gap = Ui.U(6), width = Math.Max(1, ClientSize.Width - pad * 2), y = pad;
                int dw = delete.GetPreferredSize(Size.Empty).Width;
                int lw = Math.Max(Ui.U(160), width - dw - gap), x = 0, rowY = 0, rowHeight = 0;
                foreach (Control b in left.Controls)
                {
                    Size s = b.GetPreferredSize(Size.Empty); int w = s.Width + b.Margin.Horizontal, h = s.Height + b.Margin.Vertical;
                    if (x > 0 && x + w > lw) { rowY += rowHeight; x = 0; rowHeight = 0; }
                    x += w; rowHeight = Math.Max(rowHeight, h);
                }
                int actionHeight = Math.Max(rowY + rowHeight, delete.GetPreferredSize(Size.Empty).Height);
                actions.SetBounds(pad, y, width, actionHeight); left.SetBounds(0, 0, lw, actionHeight); delete.SetBounds(width - dw, 0, dw, delete.GetPreferredSize(Size.Empty).Height); y += actionHeight + gap;
                int titleHeight = Ui.U(42); title.SetBounds(pad, y, width, titleHeight); y += titleHeight + gap;
                int line = Math.Max(Ui.U(25), meta.Font.Height + Ui.U(10)); meta.SetBounds(pad, y, width, line); y += line + gap;
                int reserve = Ui.U(140), countHeight = Math.Max(Ui.U(25), counts.Font.Height + Ui.U(10));
                int dh = Math.Min(Ui.U(160), Math.Max(Ui.U(60), (ClientSize.Height - y - reserve - countHeight - gap * 3 - pad) / 2));
                description.SetBounds(pad, y, width, dh); y += dh + gap;
                counts.SetBounds(pad, y, width, countHeight); y += countHeight + gap;
                views.SetBounds(pad, y, width, Math.Max(Ui.U(90), ClientSize.Height - y - pad));
            }
            finally { arranging = false; }
        }
    }
    internal enum Glyph { Minus, Plus, Eye, Track, Float, Settings, Close, Down, Left, Right }
    internal class IconButton : SoftButton
    {
        internal Glyph Kind; internal bool Open;
        internal IconButton(Glyph glyph)
        { Kind = glyph; BackColor = Theme.Chrome; AutoSize = false; Size = new Size(Ui.U(30), Ui.U(30)); Margin = new Padding(Ui.U(2)); Padding = Padding.Empty; Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var state = g.Save(); g.TranslateTransform(Width / 2F, Height / 2F); g.ScaleTransform(Ui.Scale, Ui.Scale);
            using (var p = new Pen(Theme.Ink, 1.45F))
            {
                p.StartCap = p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                if (Kind == Glyph.Plus || Kind == Glyph.Minus) { g.DrawLine(p, -6, 0, 6, 0); if (Kind == Glyph.Plus) g.DrawLine(p, 0, -6, 0, 6); }
                else if (Kind == Glyph.Close) { g.DrawLine(p, -4, -4, 4, 4); g.DrawLine(p, -4, 4, 4, -4); }
                else if (Kind == Glyph.Eye)
                {
                    using (var path = new GraphicsPath()) { path.AddBezier(-7, 0, -3, -6, 3, -6, 7, 0); path.AddBezier(7, 0, 3, 6, -3, 6, -7, 0); g.DrawPath(p, path); }
                    if (Open) g.DrawEllipse(p, -2, -2, 4, 4); else g.DrawLine(p, -6, 6, 6, -6);
                }
                else if (Kind == Glyph.Track) { g.DrawEllipse(p, -4, -4, 8, 8); g.DrawLine(p, -7, 0, -3, 0); g.DrawLine(p, 3, 0, 7, 0); g.DrawLine(p, 0, -7, 0, -3); g.DrawLine(p, 0, 3, 0, 7); }
                else if (Kind == Glyph.Float) { using (var path = Theme.Rounded(new RectangleF(-6, -5, 12, 10), 2)) g.DrawPath(p, path); g.DrawLine(p, -3, 2, 3, 2); g.DrawLine(p, 2, -8, 2, -6); g.DrawLine(p, 0, -7, 4, -7); }
                else if (Kind == Glyph.Settings) { g.DrawEllipse(p, -4, -4, 8, 8); g.DrawEllipse(p, -1, -1, 2, 2); for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(p, (float)Math.Cos(a) * 5, (float)Math.Sin(a) * 5, (float)Math.Cos(a) * 7, (float)Math.Sin(a) * 7); } }
                else if (Kind == Glyph.Down) g.DrawLines(p, new PointF[] { new PointF(-4,-2), new PointF(0,2), new PointF(4,-2) });
                else { float dir = Kind == Glyph.Left ? -1 : 1; g.DrawLines(p, new PointF[] { new PointF(-dir*2,-4), new PointF(dir*2,0), new PointF(-dir*2,4) }); }
            }
            g.Restore(state);
        }
    }
}

namespace TinyTodo
{
    internal sealed class ZoneSelector : SoftButton
    {
        internal readonly List<object> Items = new List<object>();
        private object selected;
        internal object SelectedItem
        {
            get { return selected; }
            set { selected = value; Text = value == null ? "系统时区" : value.ToString(); AutoSize = false;
                Width = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.U(6); Invalidate(); }
        }
        internal event EventHandler SelectionChangeCommitted;
        internal void Commit(object item) { if (!Items.Contains(item)) return; SelectedItem = item; if (SelectionChangeCommitted != null) SelectionChangeCommitted(this, EventArgs.Empty); }
        internal ZoneSelector()
        {
            Font = Theme.Font(9.5F, FontStyle.Regular, "系统时区"); Padding = new Padding(Ui.U(3)); Height = Ui.U(28); Cursor = Cursors.Hand;
            Click += delegate
            {
                var popup = new PickerPopup(Items, SelectedItem);
                popup.TopMost = FindForm() != null && FindForm().TopMost;
                var point = PointToScreen(new Point(0, Height + Ui.U(4))); Rectangle area = Screen.FromControl(this).WorkingArea;
                popup.Location = new Point(Math.Max(area.Left, Math.Min(point.X, area.Right - popup.Width)), Math.Max(area.Top, Math.Min(point.Y, area.Bottom - popup.Height)));
                popup.FormClosed += delegate { if (!IsDisposed && popup.DialogResult == DialogResult.OK) Commit(popup.Selected); };
                popup.Show(FindForm());
            };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            // Tight text-only chip: three DIP on each side, no native combo box arrow or long field.
            var g = e.Graphics; g.Clear(Parent == null ? Theme.Canvas : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), Ui.U(5)))
            using (var b = new SolidBrush(hovered ? Theme.RoseSoft : Color.White))
            using (var p = new Pen(pressed ? Theme.Rose : Theme.Border, pressed ? Ui.U(2) : 1.35F * Ui.Scale)) { g.FillPath(b, path); g.DrawPath(p, path); }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Ui.U(3), -Ui.U(1), Width - Ui.U(6), Height), Theme.Ink, TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
        private sealed class PickerPopup : Form
        {
            internal object Selected;
            internal PickerPopup(IEnumerable<object> choices, object selected)
            {
                Icon = Theme.AppIcon; AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
                ClientSize = new Size(Ui.U(360), Ui.U(340)); BackColor = Theme.Canvas; Padding = new Padding(Ui.U(7));
                Font = Theme.Font(9.5F, FontStyle.Regular, "时区");
                var search = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ForeColor = Theme.Ink, Font = Font };
                var searchHost = new SurfacePanel { Dock = DockStyle.Top, Height = Ui.U(34), Padding = new Padding(Ui.U(8)), Margin = Padding.Empty }; searchHost.Controls.Add(search);
                var list = new PickerList(choices.ToList(), selected) { Dock = DockStyle.Fill };
                list.Picked = choice => { Selected = choice; DialogResult = DialogResult.OK; Close(); };
                Controls.Add(list); Controls.Add(searchHost); search.TextChanged += delegate { list.Filter(search.Text); };
                search.KeyDown += delegate(object sender, KeyEventArgs e) { if (!Ui.ExactModifiers(e, Keys.None)) return; if (e.KeyCode == Keys.Down) { list.Focus(); e.SuppressKeyPress = true; } if (e.KeyCode == Keys.Enter) { list.PickCurrent(); e.SuppressKeyPress = true; } };
                Deactivate += delegate { Close(); }; KeyPreview = true;
                KeyDown += delegate(object sender, KeyEventArgs e) { if (Ui.ExactModifiers(e, Keys.None) && e.KeyCode == Keys.Escape) Close(); };
                using (var path = Theme.Rounded(new RectangleF(0, 0, Width, Height), Ui.U(8))) Region = new Region(path);
                Shown += delegate { search.Focus(); };
            }
        }
        private sealed class PickerList : Control
        {
            private readonly List<object> all; private List<object> rows; private int first, current;
            private readonly ThinScroll scroll = new ThinScroll(); internal Action<object> Picked;
            private int RowHeight { get { return Math.Max(Ui.U(32), Font.Height + Ui.U(12)); } }
            internal PickerList(List<object> all, object selected)
            {
                this.all = all; rows = all; current = Math.Max(0, rows.IndexOf(selected)); DoubleBuffered = true; TabStop = true; Font = Theme.Font(9.5F, FontStyle.Regular, "系统时区");
                BackColor = Color.White; scroll.Dock = DockStyle.Right; Controls.Add(scroll); scroll.Changed = value => { first = value; Invalidate(); }; Resize += delegate { Sync(); };
            }
            internal void Filter(string query) { rows = all.Where(x => x.ToString().IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList(); first = current = 0; Sync(); Invalidate(); }
            private int Page { get { return Math.Max(1, Height / RowHeight); } }
            private void Sync() { first = Math.Max(0, Math.Min(first, rows.Count - Page)); scroll.SetRange(Math.Max(0, rows.Count - Page), Page, first); }
            internal void PickCurrent() { if (rows.Count > 0 && Picked != null) Picked(rows[Math.Min(current, rows.Count - 1)]); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Color.White); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = first; i < Math.Min(rows.Count, first + Page + 1); i++)
                {
                    var r = new Rectangle(2, (i - first) * RowHeight + 2, Width - Ui.U(14), RowHeight - 4);
                    if (i == current) using (var path = Theme.Rounded(r, Ui.U(5))) using (var b = new SolidBrush(Theme.RoseSoft)) e.Graphics.FillPath(b, path);
                    r.Inflate(-Ui.U(7), 0); TextRenderer.DrawText(e.Graphics, rows[i].ToString(), Font, r, Theme.Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int row = first + e.Y / RowHeight; if (row >= 0 && row < rows.Count) { current = row; Invalidate(); } }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) { int row = first + e.Y / RowHeight; if (row >= 0 && row < rows.Count) { current = row; PickCurrent(); } } }
            protected override void OnMouseWheel(MouseEventArgs e) { first -= e.Delta / 120 * 3; Sync(); Invalidate(); base.OnMouseWheel(e); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (!Ui.ExactModifiers(e, Keys.None)) { base.OnKeyDown(e); return; }
                if (e.KeyCode == Keys.Down) current = Math.Min(rows.Count - 1, current + 1);
                else if (e.KeyCode == Keys.Up) current = Math.Max(0, current - 1);
                else if (e.KeyCode == Keys.Enter) PickCurrent(); else { base.OnKeyDown(e); return; }
                if (current < first) first = current; if (current >= first + Page) first = current - Page + 1; Sync(); Invalidate(); e.SuppressKeyPress = true;
            }
            protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || base.IsInputKey(key); }
        }
    }
}

namespace TinyTodo
{
    internal sealed class AddTaskButton : SoftButton
    {
        private readonly ToolTip tip = new ToolTip();
        internal AddTaskButton()
        {
            AutoSize = false; Size = new Size(Ui.U(24), Ui.U(24));
            int tabHeight = Math.Max(Ui.U(34), Theme.Font(9.5F, FontStyle.Regular, "待办历史").Height + Ui.U(8));
            // Inactive tabs start 5 DIP below their control top; align with that visible band.
            int top = Ui.U(3) + (Ui.U(5) + tabHeight - 1 - Height) / 2;
            Margin = new Padding(Ui.U(12), top, Ui.U(3), 0);
            BackColor = Theme.AddAction; ForeColor = Theme.Ink; Cursor = Cursors.Hand;
            AccessibleName = "新增"; AccessibleDescription = "添加任务";
            tip.SetToolTip(this, "新增任务");
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent == null ? Theme.Canvas : Parent.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float side = Math.Min(Width, Height) - Ui.U(2), cx = Width / 2F, cy = Height / 2F;
            Color fill = pressed ? Theme.Mix(Theme.Rose, BackColor, .12F) : hovered ? Theme.Mix(Color.White, BackColor, .12F) : BackColor;
            using (var brush = new SolidBrush(fill)) g.FillEllipse(brush, cx - side / 2, cy - side / 2, side, side);
            using (var pen = new Pen(Enabled ? ForeColor : Theme.Muted, 2.4F * Ui.Scale))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                float arm = Ui.U(4);
                g.DrawLine(pen, cx - arm, cy, cx + arm, cy);
                g.DrawLine(pen, cx, cy - arm, cx, cy + arm);
            }
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Theme.Latte, Ui.Scale)) g.DrawEllipse(pen, cx - side / 2, cy - side / 2, side, side);
        }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class MainToolbar : Panel
    {
        private readonly FlowLayoutPanel left = Ui.Bar(); private Control navigation;
        internal Control.ControlCollection LeftItems { get { return left.Controls; } }
        internal MainToolbar() { Dock = DockStyle.Fill; Height = Ui.U(40); AutoSize = true; left.Dock = DockStyle.None; left.AutoSize = false; Controls.Add(left); }
        internal void SetNavigation(Control value) { navigation = value; navigation.Dock = DockStyle.None; navigation.AutoSize = false; Controls.Add(navigation); PerformLayout(); }
        private static int StripHeight(Control strip)
        {
            if (strip == null) return 0;
            int height = 0;
            foreach (Control child in strip.Controls) height = Math.Max(height, child.GetPreferredSize(Size.Empty).Height + child.Margin.Vertical);
            return height;
        }
        public override Size GetPreferredSize(Size proposedSize)
        { return new Size(proposedSize.Width, Math.Max(StripHeight(left), StripHeight(navigation)) + Ui.ViewTabGap); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (left == null) return;
            int navWidth = 0; if (navigation != null) foreach (Control c in navigation.Controls) navWidth += c.GetPreferredSize(Size.Empty).Width + c.Margin.Horizontal;
            left.SetBounds(0, 0, Math.Max(1, Width - navWidth - Ui.U(10)), Ui.U(40));
            if (navigation != null) navigation.SetBounds(Math.Max(0, Width - navWidth), 0, navWidth, Ui.U(40));
        }
    }
}

namespace TinyTodo
{
    internal sealed class FloatingSwitch : Control
    {
        internal Action<bool> Changed;
        private bool pressed, isFloating;
        private float progress, transitionFrom;
        private readonly System.Diagnostics.Stopwatch transitionClock = new System.Diagnostics.Stopwatch();
        private Timer transitionTimer;
        private readonly Bitmap catIcon;
        internal bool IsFloating
        {
            get { return isFloating; }
            set
            {
                if (isFloating == value) return;
                AdvanceTransition(); isFloating = value;
                if (IsHandleCreated && Visible && Enabled && transitionTimer != null)
                {
                    transitionFrom = progress; transitionClock.Restart(); transitionTimer.Start();
                }
                else FinishTransition();
                Invalidate();
            }
        }
        internal FloatingSwitch()
        {
            DoubleBuffered = true; Size = new Size(Ui.U(56), Ui.U(30)); BackColor = Theme.Chrome; TabStop = true; Cursor = Cursors.Hand;
            string iconPath = Theme.Asset("cat-toggle.png");
            if (System.IO.File.Exists(iconPath))
                using (var source = Image.FromFile(iconPath)) catIcon = Theme.FitIcon(source, Ui.U(19), InterpolationMode.HighQualityBicubic, 8);
            transitionTimer = new Timer { Interval = 15 };
            transitionTimer.Tick += delegate { AdvanceTransition(); Invalidate(); };
        }
        private void AdvanceTransition()
        {
            if (transitionTimer == null || !transitionTimer.Enabled) return;
            float elapsed = Math.Min(1F, transitionClock.ElapsedMilliseconds / 180F);
            float eased = 1F - (float)Math.Pow(1F - elapsed, 3);
            progress = transitionFrom + ((isFloating ? 1F : 0F) - transitionFrom) * eased;
            if (elapsed >= 1F) FinishTransition();
        }
        private void FinishTransition()
        {
            if (transitionTimer != null) transitionTimer.Stop();
            transitionClock.Reset(); progress = isFloating ? 1F : 0F;
        }
        protected override void OnVisibleChanged(EventArgs e)
        { if (!Visible) FinishTransition(); base.OnVisibleChanged(e); }
        protected override void OnEnabledChanged(EventArgs e)
        { if (!Enabled) FinishTransition(); Invalidate(); base.OnEnabledChanged(e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing && transitionTimer != null) { transitionTimer.Dispose(); transitionTimer = null; }
            if (disposing && catIcon != null) catIcon.Dispose();
            base.Dispose(disposing);
        }
        internal void Choose(bool value) { if (value != IsFloating && Changed != null) Changed(value); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { bool choose = pressed && ClientRectangle.Contains(e.Location); pressed = false; Invalidate(); base.OnMouseUp(e); if (choose) Choose(!IsFloating); }
        protected override void OnKeyDown(KeyEventArgs e)
        { if (Ui.ExactModifiers(e, Keys.None) && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Space)) { Choose(e.KeyCode == Keys.Space ? !IsFloating : e.KeyCode == Keys.Right); e.SuppressKeyPress = true; } base.OnKeyDown(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) pressed = false; Invalidate(); base.OnMouseCaptureChanged(e); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || base.IsInputKey(key); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Canvas); g.SmoothingMode = SmoothingMode.AntiAlias;
            Color outline = Theme.Mix(Theme.Rose, Theme.Border, progress);
            Color track = Theme.Mix(Theme.RoseSoft, Theme.Chrome, progress * .22F);
            Color thumb = Theme.Mix(Theme.AddAction, Theme.HeaderTint, progress);
            if (!Enabled) { outline = Theme.Border; track = Theme.Chrome; thumb = Theme.Done; }
            var trackBounds = new RectangleF(Ui.Scale, Ui.Scale, Width - Ui.Scale * 2 - 1, Height - Ui.Scale * 2 - 1);
            using (var path = Theme.Rounded(trackBounds, Height / 2F))
            using (var brush = new SolidBrush(track))
            using (var pen = new Pen(pressed ? Theme.Rose : outline, pressed ? Ui.U(2) : 1F + progress * Ui.Scale * .5F))
            { g.FillPath(brush, path); g.DrawPath(pen, path); }
            // Inset the unchanged track uniformly so both capsule ends share their arc centers.
            float inset = Ui.Scale * 2F, knobWidth = Ui.U(31);
            float knobHeight = trackBounds.Height - inset * 2F;
            float left = trackBounds.Left + inset + (trackBounds.Width - knobWidth - inset * 2F) * progress;
            var knob = new RectangleF(left, trackBounds.Top + inset, knobWidth, knobHeight);
            using (var path = Theme.Rounded(knob, knob.Height / 2F))
            using (var brush = new SolidBrush(thumb)) g.FillPath(brush, path);
            float icon = Ui.U(19);
            DrawCatIcon(g, new RectangleF(knob.X + (knob.Width - icon) / 2F, knob.Y + (knob.Height - icon) / 2F, icon, icon));
        }
        private void DrawCatIcon(Graphics g, RectangleF bounds)
        {
            if (catIcon == null) return;
            // Keep the supplied alpha silhouette; blend its brown into the canvas color when active.
            using (var attributes = new System.Drawing.Imaging.ImageAttributes())
            {
                var matrix = new System.Drawing.Imaging.ColorMatrix();
                float active = Enabled ? progress : 0F;
                matrix.Matrix00 = matrix.Matrix11 = matrix.Matrix22 = 1F - active;
                matrix.Matrix40 = Theme.Canvas.R / 255F * active;
                matrix.Matrix41 = Theme.Canvas.G / 255F * active;
                matrix.Matrix42 = Theme.Canvas.B / 255F * active;
                matrix.Matrix33 = Enabled ? .64F + progress * .36F : .32F;
                attributes.SetColorMatrix(matrix);
                var target = new PointF[] { new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Top), new PointF(bounds.Left, bounds.Bottom) };
                g.DrawImage(catIcon, target, new RectangleF(0, 0, catIcon.Width, catIcon.Height), GraphicsUnit.Pixel, attributes);
            }
        }
    }
}

namespace TinyTodo
{
    internal sealed class HoverMarkdown : IDisposable
    {
        private readonly Timer timer = new Timer { Interval = 450 };
        private readonly Timer monitor = new Timer { Interval = 100 };
        private readonly System.Diagnostics.Stopwatch outside = new System.Diagnostics.Stopwatch();
        private Control source; private Todo task; private Point point; private HoverCard card;
        private Func<Point, bool> hitTest;
        internal HoverMarkdown()
        {
            timer.Tick += delegate { timer.Stop(); Show(); };
            monitor.Tick += delegate { CheckPointer(); };
        }
        internal void Schedule(Control source, Todo task, Point point, Func<Point, bool> hitTest = null)
        {
            // Returning from the card to its original task must not rebuild the popup.
            if (card != null && !card.IsDisposed && this.source == source && this.task.Id == task.Id)
            { this.hitTest = hitTest; outside.Reset(); return; }
            Hide(); this.source = source; this.task = task; this.point = point; this.hitTest = hitTest;
            timer.Start();
        }
        private bool SourceAvailable()
        {
            if (source == null || source.IsDisposed || !source.IsHandleCreated || !source.Visible) return false;
            Form owner = source.FindForm();
            return owner != null && !owner.IsDisposed && owner.Visible && owner.WindowState != FormWindowState.Minimized;
        }
        private bool OverSource()
        {
            if (!SourceAvailable()) return false;
            Point local = source.PointToClient(Cursor.Position);
            return source.ClientRectangle.Contains(local) && (hitTest == null || hitTest(local));
        }
        private void Show()
        {
            if (!SourceAvailable() || task == null || !DesktopActivity.OwnsForeground || !OverSource()) return;
            card = new HoverCard(task); Rectangle area = Screen.FromPoint(point).WorkingArea;
            card.Location = new Point(Math.Max(area.Left, Math.Min(point.X + Ui.U(12), area.Right - card.Width)), Math.Max(area.Top, Math.Min(point.Y + Ui.U(20), area.Bottom - card.Height)));
            var shown = card;
            card.FormClosed += delegate
            {
                if (Object.ReferenceEquals(card, shown)) { card = null; monitor.Stop(); outside.Reset(); }
            };
            card.Show(source.FindForm()); outside.Reset(); monitor.Start();
        }
        private void CheckPointer()
        {
            if (card == null || card.IsDisposed) { monitor.Stop(); return; }
            if (!SourceAvailable() || !DesktopActivity.OwnsForeground) { Hide(); return; }
            if (card.Bounds.Contains(Cursor.Position) || OverSource()) { outside.Reset(); return; }
            // Allow crossing the small gap between the task and the popup.
            if (!outside.IsRunning) outside.Start();
            else if (outside.ElapsedMilliseconds >= 180) Hide();
        }
        internal void Leave()
        {
            timer.Stop();
            if (card != null) CheckPointer();
        }
        internal void Hide()
        {
            timer.Stop(); monitor.Stop(); outside.Reset();
            var old = card; card = null;
            source = null; task = null; hitTest = null;
            if (old != null) { old.Close(); old.Dispose(); }
        }
        public void Dispose() { Hide(); timer.Dispose(); monitor.Dispose(); }
        private sealed class HoverCard : Form, IMessageFilter
        {
            private readonly TextViewport viewport;
            protected override bool ShowWithoutActivation { get { return true; } }
            public bool PreFilterMessage(ref Message message)
            {
                // Route the wheel into this card without taking focus from the main window.
                if (message.Msg != 0x20A || !Visible || !Bounds.Contains(Cursor.Position)) return false;
                int delta = (short)((message.WParam.ToInt64() >> 16) & 0xffff);
                viewport.ScrollWheel(delta); return true;
            }
            protected override void Dispose(bool disposing)
            { if (disposing) Application.RemoveMessageFilter(this); base.Dispose(disposing); }
            internal HoverCard(Todo task)
            {
                Icon = Theme.AppIcon; AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
                ClientSize = new Size(Ui.U(330), Ui.U(210)); BackColor = Theme.Canvas; Padding = new Padding(Ui.U(6));
                var title = new ReadOnlyText { Text = task.Name, ForeColor = Theme.Ink, BackColor = Theme.Canvas, Font = Theme.Font(10, FontStyle.Bold, task.Name), Dock = DockStyle.Top, Height = Ui.U(30), Multiline = true };
                var preview = new MarkdownView(); viewport = new TextViewport(preview); preview.ShowMarkdown(task.Description);
                Controls.Add(viewport); Controls.Add(title); Application.AddMessageFilter(this);
                using (var path = Theme.Rounded(new RectangleF(0, 0, Width, Height), Ui.U(8))) Region = new Region(path);
            }
        }
    }
}

namespace TinyTodo
{
    internal sealed class InputSurface : SurfacePanel
    {
        internal InputSurface(TextBox field)
        { field.Font = Theme.Font(9.5F, FontStyle.Regular, "任务名称"); Dock = DockStyle.Top; Height = Math.Max(Ui.U(34), field.Font.Height + Ui.U(14)); Padding = new Padding(Ui.U(7)); field.BorderStyle = BorderStyle.None; field.ForeColor = Theme.Ink; field.Dock = DockStyle.Fill; Controls.Add(field); }
        internal static Control Wrap(Control control) { var surface = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(Ui.U(6)) }; surface.Controls.Add(control); return surface; }
    }
    internal sealed class DateInput : SurfacePanel
    {
        private readonly TextBox field = new TextBox(); private readonly IconButton calendar = new IconButton(Glyph.Down);
        private bool enabledDate; private DateTime date = DateTime.Today;
        internal DateTime Value
        {
            get { DateTime parsed; if (!DateTime.TryParseExact(field.Text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out parsed)) throw new InvalidOperationException("日期请填写 yyyy-mm-dd。"); return parsed; }
            set { date = value.Date; field.Text = date.ToString("yyyy-MM-dd"); }
        }
        internal bool Checked { get { return enabledDate; } set { enabledDate = value; field.Enabled = value; Invalidate(); } }
        internal DateInput()
        {
            Height = Ui.U(36); Padding = new Padding(Ui.U(34), Ui.U(7), Ui.U(34), Ui.U(5));
            field.BorderStyle = BorderStyle.None; field.ForeColor = Theme.Ink; field.BackColor = Color.White; field.Font = Theme.Font(9.5F, FontStyle.Regular, "2026-09-15"); field.Dock = DockStyle.Fill;
            field.TextChanged += delegate { DateTime parsed; if (DateTime.TryParseExact(field.Text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out parsed)) date = parsed; };
            Controls.Add(field); Controls.Add(calendar); Value = date; Checked = false;
            calendar.Click += delegate
            {
                var popup = new CalendarPopup(date);
                popup.TopMost = FindForm() != null && FindForm().TopMost;
                Rectangle area = Screen.FromControl(this).WorkingArea; Point point = PointToScreen(new Point(0, Height));
                popup.Location = new Point(Math.Max(area.Left, Math.Min(point.X, area.Right - popup.Width)), Math.Max(area.Top, Math.Min(point.Y, area.Bottom - popup.Height)));
                popup.FormClosed += delegate { if (!IsDisposed && popup.DialogResult == DialogResult.OK) { Value = popup.Value; Checked = true; } };
                popup.Show(FindForm());
            };
        }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); if (calendar != null) calendar.SetBounds(Width - Ui.U(32), Ui.U(3), Ui.U(28), Ui.U(28)); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Theme.Check(e.Graphics, new RectangleF(Ui.U(10), (Height - Ui.U(14)) / 2, Ui.U(14), Ui.U(14)), Checked); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left && e.X < Ui.U(32)) Checked = !Checked; }
        private sealed class CalendarPopup : Form
        {
            internal DateTime Value; private DateTime month; private int focusDay;
            internal CalendarPopup(DateTime value)
            {
                Value = value; month = new DateTime(value.Year, value.Month, 1); focusDay = value.Day;
                Icon = Theme.AppIcon; AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; ClientSize = new Size(Ui.U(266), Ui.U(278)); BackColor = Color.White; DoubleBuffered = true;
                Font = Theme.Font(9.5F, FontStyle.Regular, "年月日");
                var previous = new IconButton(Glyph.Left) { Location = new Point(Ui.U(8), Ui.U(6)) }; var next = new IconButton(Glyph.Right) { Location = new Point(Width - Ui.U(38), Ui.U(6)) };
                previous.Click += delegate { if (month.Year > 1 || month.Month > 1) { month = month.AddMonths(-1); focusDay = 1; Invalidate(); } };
                next.Click += delegate { if (month.Year < 9999 || month.Month < 12) { month = month.AddMonths(1); focusDay = 1; Invalidate(); } };
                Controls.Add(previous); Controls.Add(next); Deactivate += delegate { Close(); }; KeyPreview = true;
                using (var path = Theme.Rounded(new RectangleF(0, 0, Width, Height), Ui.U(8))) Region = new Region(path);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                TextRenderer.DrawText(e.Graphics, month.ToString("yyyy · MM"), Font, new Rectangle(0, Ui.U(4), Width, Ui.U(34)), Theme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                string[] labels = { "一", "二", "三", "四", "五", "六", "日" }; int cell = Ui.U(36), start = ((int)month.DayOfWeek + 6) % 7;
                for (int i = 0; i < 7; i++) TextRenderer.DrawText(e.Graphics, labels[i], Font, new Rectangle(Ui.U(7) + cell * i, Ui.U(39), cell, Ui.U(22)), Theme.Muted, TextFormatFlags.HorizontalCenter);
                for (int day = 1; day <= DateTime.DaysInMonth(month.Year, month.Month); day++)
                {
                    int n = start + day - 1; var r = new Rectangle(Ui.U(7) + n % 7 * cell, Ui.U(62) + n / 7 * Ui.U(34), cell - Ui.U(2), Ui.U(30));
                    if (day == focusDay) using (var path = Theme.Rounded(r, Ui.U(5))) using (var brush = new SolidBrush(Theme.RoseSoft)) e.Graphics.FillPath(brush, path);
                    TextRenderer.DrawText(e.Graphics, day.ToString(), Font, r, Theme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e); if (e.Button != MouseButtons.Left || e.Y < Ui.U(62) || e.X < Ui.U(7) || e.X >= Ui.U(259)) return;
                int day = (e.Y - Ui.U(62)) / Ui.U(34) * 7 + (e.X - Ui.U(7)) / Ui.U(36) - ((int)month.DayOfWeek + 6) % 7 + 1;
                if (day < 1 || day > DateTime.DaysInMonth(month.Year, month.Month)) return; Value = new DateTime(month.Year, month.Month, day); DialogResult = DialogResult.OK; Close();
            }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (!Ui.ExactModifiers(e, Keys.None)) { base.OnKeyDown(e); return; }
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Enter) { Value = new DateTime(month.Year, month.Month, focusDay); DialogResult = DialogResult.OK; Close(); }
                else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) { focusDay = Math.Max(1, Math.Min(DateTime.DaysInMonth(month.Year, month.Month), focusDay + (e.KeyCode == Keys.Left ? -1 : 1))); Invalidate(); }
                base.OnKeyDown(e);
            }
        }
    }
}

namespace TinyTodo
{
    internal sealed class ScrollSurface : Panel
    {
        private readonly Control content; private readonly ThinScroll scroll = new ThinScroll(); private readonly int minimum; private int offset; private bool arranging;
        internal ScrollSurface(Control content, int minimum)
        {
            this.content = content; this.minimum = minimum; Dock = DockStyle.Fill; content.Dock = DockStyle.None;
            Controls.Add(content); Controls.Add(scroll); scroll.BackColor = Theme.Canvas; scroll.Changed = value => { offset = value; Arrange(); };
            Watch(content);
        }
        private void Watch(Control control)
        {
            control.Enter += delegate
            {
                if (!control.IsHandleCreated || !IsHandleCreated) return;
                Rectangle r = RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                if (r.Top < 0) offset = Math.Max(0, offset + r.Top);
                else if (r.Bottom > ClientSize.Height) offset += r.Bottom - ClientSize.Height;
                Arrange();
            };
            foreach (Control child in control.Controls) Watch(child);
        }
        private void Arrange()
        {
            if (content == null || arranging) return; arranging = true;
            try { int height = Math.Max(minimum, ClientSize.Height), max = Math.Max(0, height - ClientSize.Height); offset = Math.Max(0, Math.Min(max, offset)); scroll.SetRange(max, Math.Max(1, ClientSize.Height), offset);
                scroll.SetBounds(ClientSize.Width - scroll.Width, 0, scroll.Width, ClientSize.Height); content.SetBounds(0, -offset, Math.Max(1, ClientSize.Width - (max > 0 ? scroll.Width : 0)), height); }
            finally { arranging = false; }
        }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); Arrange(); }
        protected override void OnMouseWheel(MouseEventArgs e) { offset -= e.Delta / 120 * Ui.U(40); Arrange(); base.OnMouseWheel(e); }
    }
}
