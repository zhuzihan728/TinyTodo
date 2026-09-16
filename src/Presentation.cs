using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace TinyTodo
{
    internal sealed class TaskMenuRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        { e.Graphics.Clear(Theme.Canvas); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using (var path = Theme.Rounded(new RectangleF(1, 1, e.ToolStrip.Width - 3, e.ToolStrip.Height - 3), Ui.U(7))) using (var b = new SolidBrush(Color.White)) e.Graphics.FillPath(b, path); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        { using (var path = Theme.Rounded(new RectangleF(1, 1, e.ToolStrip.Width - 3, e.ToolStrip.Height - 3), Ui.U(7))) using (var p = new Pen(Theme.Border)) e.Graphics.DrawPath(p, path); }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(2, 1, e.Item.Width - 5, e.Item.Height - 3), Ui.U(5)))
            using (var brush = new SolidBrush(Theme.RoseSoft)) e.Graphics.FillPath(brush, path);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont,
                new Rectangle(Ui.U(9), 0, e.Item.Width - Ui.U(18), e.Item.Height),
                e.Item.Enabled ? Theme.Ink : Theme.Muted,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
        }
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) { }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, Ui.U(8), e.Item.Height / 2, e.Item.Width - Ui.U(8), e.Item.Height / 2); }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    }
    internal class SoftMenu : ContextMenuStrip
    {
        internal SoftMenu()
        { Renderer = new TaskMenuRenderer(); ShowImageMargin = ShowCheckMargin = false; DropShadowEnabled = false; BackColor = Color.White; ForeColor = Theme.Ink; Font = Theme.Font(9.5F, FontStyle.Regular, "设置任务"); Padding = new Padding(Ui.U(5)); }
        protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
        {
            int width = Ui.U(125);
            foreach (ToolStripItem item in Items)
            { item.Font = Font; item.ForeColor = Theme.Ink; width = Math.Max(width, TextRenderer.MeasureText(item.Text, Font).Width + Ui.U(36)); }
            foreach (ToolStripItem item in Items)
            { item.AutoSize = false; item.Size = new Size(width, item is ToolStripSeparator ? Ui.U(9) : Math.Max(Ui.U(31), Font.Height + Ui.U(12))); }
            base.OnOpening(e);
        }
        protected override void OnSizeChanged(EventArgs e)
        { base.OnSizeChanged(e); if (Width < 2 || Height < 2) return; using (var path = Theme.Rounded(new RectangleF(0, 0, Width, Height), Ui.U(7))) { var old = Region; Region = new Region(path); if (old != null) old.Dispose(); } }
    }
    // Keep native text selection, shortcuts and clipboard support, without an editing caret.
    internal sealed class ReadOnlyText : TextBox
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool HideCaret(IntPtr handle);
        internal ReadOnlyText()
        { ReadOnly = true; BorderStyle = BorderStyle.None; Cursor = Cursors.Default; HideSelection = false; }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x20) { Cursor.Current = Cursors.Default; message.Result = new IntPtr(1); return; }
            base.WndProc(ref message); if (Focused) HideCaret(Handle);
        }
    }
    internal sealed class MarkdownView : RichTextBox
    {
        private readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        private string source;
        private sealed class LinkRange { internal int Start, Length; internal string Target; internal Font Font; }
        private readonly List<LinkRange> links = new List<LinkRange>();
        private readonly ToolTip linkTip = new ToolTip();
        private string hoveredLink;
        private Point mouseDown;
        internal Action<string> OpenLink;
        internal string LinkAt(int index) { var link = links.FirstOrDefault(x => index >= x.Start && index < x.Start + x.Length); return link == null ? null : link.Target; }
        internal bool ActivateLink(int index)
        {
            string target = LinkAt(index); if (!SimpleMarkdown.IsWebLink(target)) return false;
            try { if (OpenLink != null) OpenLink(target); else Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { ThemedDialog.Show(this, "无法打开链接：" + ex.Message, "TinyTodo", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            return true;
        }
        private int HitLink(Point point)
        {
            int index = GetCharIndexFromPosition(point); if (index >= TextLength || LinkAt(index) == null) return -1;
            Point start = GetPositionFromCharIndex(index);
            Font font = links.First(x => index >= x.Start && index < x.Start + x.Length).Font;
            Size extent = TextRenderer.MeasureText(Text.Substring(index, 1), font, Size.Empty, TextFormatFlags.NoPadding);
            if (point.Y < start.Y || point.Y > start.Y + font.Height + Ui.U(3) || point.X < start.X || point.X > start.X + extent.Width + Ui.U(3)) return -1;
            return index;
        }
        protected override void OnMouseDown(MouseEventArgs e) { mouseDown = e.Location; base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); int index = HitLink(e.Location); string target = index < 0 ? null : LinkAt(index);
            Cursor = target == null ? Cursors.Default : Cursors.Hand;
            if (hoveredLink != target) { hoveredLink = target; linkTip.SetToolTip(this, target); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && Math.Abs(e.X - mouseDown.X) < Ui.U(4) && Math.Abs(e.Y - mouseDown.Y) < Ui.U(4))
            { int index = HitLink(e.Location); if (index >= 0) ActivateLink(index); }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        { if (Ui.ExactModifiers(e, Keys.Control) && e.KeyCode == Keys.Enter && ActivateLink(SelectionStart)) { e.SuppressKeyPress = true; return; } base.OnKeyDown(e); }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool HideCaret(IntPtr handle);
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x20)
            { Cursor.Current = HitLink(PointToClient(Cursor.Position)) < 0 ? Cursors.Default : Cursors.Hand; message.Result = new IntPtr(1); return; }
            base.WndProc(ref message); if (Focused && ReadOnly) HideCaret(Handle);
        }
        internal MarkdownView()
        {
            Dock = DockStyle.Fill; ReadOnly = true; BorderStyle = BorderStyle.None; HideSelection = false; Cursor = Cursors.Default;
            DetectUrls = false; BackColor = Color.White; ForeColor = Ui.Ink;
            ScrollBars = RichTextBoxScrollBars.Vertical; Font = Theme.Font(9.5F, FontStyle.Regular, "任务描述");
            AccessibleName = "Markdown 描述预览";
        }
        internal void ShowMarkdown(string markdown)
        {
            markdown = markdown ?? ""; if (source == markdown) return;
            source = markdown; links.Clear(); Clear(); SuspendLayout();
            try
            {
                var lines = SimpleMarkdown.Parse(markdown);
                for (int i = 0; i < lines.Count; i++)
                {
                    MarkdownLine line = lines[i];
                    if (line.Prefix.Length > 0) Append(line.Prefix, line, false, false, false);
                    foreach (MarkdownRun run in line.Runs) Append(run.Text, line, run.Bold, run.Italic, run.Code, run.Link, run.Strike);
                    if (i + 1 < lines.Count) Append("\n", line, false, false, line.Code);
                }
                Select(0, 0); ScrollToCaret();
            }
            finally { ResumeLayout(); }
        }
        private void Append(string text, MarkdownLine line, bool bold, bool italic, bool code, string link = null, bool strike = false)
        {
            float size = line.Heading == 1 ? 14F : line.Heading == 2 ? 12F : line.Heading == 3 ? 10.5F : 9.5F;
            FontStyle style = (bold || line.Heading > 0 ? FontStyle.Bold : FontStyle.Regular) | (italic ? FontStyle.Italic : FontStyle.Regular);
            if (link != null) style |= FontStyle.Underline; if (strike) style |= FontStyle.Strikeout;
            string family = code ? "Consolas" : Theme.Font(size, style, text).Name;
            string key = family + ":" + size + ":" + style; Font font;
            if (!fonts.TryGetValue(key, out font)) { font = code ? new Font(family, size, style) : (Font)Theme.Font(size, style, text).Clone(); fonts.Add(key, font); }
            SelectionStart = TextLength; SelectionLength = 0;
            SelectionFont = font; SelectionColor = link != null ? Theme.Teal : line.Quote ? Theme.Quote : line.Heading > 0 ? Theme.Rose : Ui.Ink;
            SelectionBackColor = code ? Theme.Done : line.Quote ? Theme.QuoteSoft : BackColor;
            if (link != null && text.Length > 0) links.Add(new LinkRange { Start = TextLength, Length = text.Length, Target = link, Font = font });
            AppendText(text);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { linkTip.Dispose(); foreach (Font font in fonts.Values) font.Dispose(); } base.Dispose(disposing); }
    }
}
