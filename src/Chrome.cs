using System;
using System.Drawing;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TinyTodo
{
    // One caption and frame shared by all app windows. Client controls never overlap it.
    internal class AppWindow : Form
    {
        private readonly IconButton closeButton, minimizeButton;
        private readonly List<Control> captionActions = new List<Control>();
        private readonly List<Control> grips = new List<Control>();
        private readonly ToolTip captionTips = new ToolTip();
        internal IconButton AddCaptionAction(Glyph glyph, string name, EventHandler click)
        { var b = new IconButton(glyph) { AccessibleName = name }; b.Click += click; captionTips.SetToolTip(b, name); captionActions.Add(b); Controls.Add(b); PerformLayout(); return b; }
        internal FloatingSwitch AddFloatingSwitch(Action<bool> changed)
        { var b = new FloatingSwitch { AccessibleName = "收起猫猫" }; b.Changed = changed; captionActions.Add(b); Controls.Add(b); PerformLayout(); return b; }
        internal bool CanResize = true;
        internal int CaptionHeight { get { return Ui.U(40); } }
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
        internal AppWindow()
        {
            Icon = Theme.AppIcon; AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            Padding = new Padding(Ui.U(3), CaptionHeight, Ui.U(3), Ui.U(3));
            closeButton = new IconButton(Glyph.Close) { AccessibleName = "关闭" };
            minimizeButton = new IconButton(Glyph.Minus) { AccessibleName = "最小化" };
            closeButton.Click += delegate { Close(); };
            minimizeButton.Click += delegate { WindowState = FormWindowState.Minimized; };
            Controls.Add(closeButton); Controls.Add(minimizeButton);
            TextChanged += delegate { Invalidate(); };
            for (int i = 0; i < 4; i++) { var grip = new CornerGrip(this, i); grips.Add(grip); Controls.Add(grip); }
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (closeButton == null) return;
            int side = Ui.U(30), y = (CaptionHeight - side) / 2;
            closeButton.SetBounds(ClientSize.Width - side - Ui.U(18), y, side, side);
            minimizeButton.SetBounds(closeButton.Left - side - Ui.U(6), y, side, side);
            minimizeButton.Visible = MinimizeBox; closeButton.BringToFront(); minimizeButton.BringToFront();
            int next = MinimizeBox ? minimizeButton.Left : closeButton.Left;
            for (int i = captionActions.Count - 1; i >= 0; i--) { int w = captionActions[i] is FloatingSwitch ? Ui.U(90) : side; next -= w + Ui.U(4); captionActions[i].SetBounds(next, y, w, side); captionActions[i].BringToFront(); }
            int size = Ui.U(18);
            for (int i = 0; i < grips.Count; i++) { grips[i].SetBounds(i % 2 == 0 ? 0 : Width - size, i < 2 ? 0 : Height - size, size, size); grips[i].Visible = CanResize; grips[i].BringToFront(); }
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
            using (var p = Theme.Rounded(new RectangleF(0, 0, Width, Height), Ui.U(16)))
            { var previous = Region; Region = new Region(p); if (previous != null) previous.Dispose(); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), Ui.U(16)))
            using (var pen = new Pen(Theme.Border, Ui.U(1))) e.Graphics.DrawPath(pen, path);
            int x = Ui.U(16);
            if (Theme.Logo != null)
            {
                int size = Ui.U(34);
                e.Graphics.DrawImage(Theme.Logo, x, (CaptionHeight - size) / 2, size, size); x += size + Ui.U(8);
            }
            int right = captionActions.Count > 0 ? captionActions[0].Left : MinimizeBox ? minimizeButton.Left : closeButton.Left;
            TextRenderer.DrawText(e.Graphics, Text, Theme.Font(10, FontStyle.Bold, Text), new Rectangle(x, 0, Math.Max(1, right - x - Ui.U(12)), CaptionHeight), Theme.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < CaptionHeight)
            { ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); }
        }
        internal int EdgeHit(Point p)
        {
            if (!CanResize || WindowState != FormWindowState.Normal) return 1;
            int corner = Ui.U(20);
            if (p.X < corner && p.Y < corner) return 13;
            if (p.X >= Width - corner && p.Y < corner) return 14;
            if (p.X < corner && p.Y >= Height - corner) return 16;
            if (p.X >= Width - corner && p.Y >= Height - corner) return 17;
            int grip = Ui.U(6); bool l = p.X < grip, r = p.X >= ClientSize.Width - grip;
            bool t = p.Y < grip, b = p.Y >= ClientSize.Height - grip;
            return t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 1;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84)
            {
                long lp = m.LParam.ToInt64(); var p = PointToClient(new Point((short)(lp & 0xffff), (short)((lp >> 16) & 0xffff)));
                int hit = EdgeHit(p); if (hit != 1) { m.Result = new IntPtr(hit); return; }
            }
            base.WndProc(ref m);
        }
        private sealed class CornerGrip : Control
        {
            private readonly AppWindow owner; private readonly int corner;
            internal CornerGrip(AppWindow owner, int corner)
            { this.owner = owner; this.corner = corner; SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; Cursor = corner == 0 || corner == 3 ? Cursors.SizeNWSE : Cursors.SizeNESW; }
            protected override void OnMouseDown(MouseEventArgs e)
            { base.OnMouseDown(e); if (e.Button != MouseButtons.Left || !owner.CanResize) return; ReleaseCapture(); SendMessage(owner.Handle, 0x112, new IntPtr(0xF000 + (corner == 0 ? 4 : corner == 1 ? 5 : corner == 2 ? 7 : 8)), IntPtr.Zero); }
        }
        protected override void Dispose(bool disposing) { if (disposing) captionTips.Dispose(); base.Dispose(disposing); }
    }

    internal class SurfacePanel : Panel
    {
        internal SurfacePanel() { DoubleBuffered = true; BackColor = Color.White; Padding = new Padding(Ui.U(8)); }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e); if (Width < 2 || Height < 2) return;
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width, Height), Ui.U(12)))
            { var previous = Region; Region = new Region(path); if (previous != null) previous.Dispose(); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.Rounded(new RectangleF(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), Ui.U(12)))
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawPath(pen, p);
        }
    }
    internal static class ThemedDialog
    {
        internal static DialogResult Show(string text, string caption = "TinyTodo", MessageBoxButtons buttons = MessageBoxButtons.OK,
            MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        { return Show(null, text, caption, buttons, icon, defaultButton); }
        internal static DialogResult Show(IWin32Window owner, string text, string caption = "TinyTodo", MessageBoxButtons buttons = MessageBoxButtons.OK,
            MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        {
            using (var dialog = new AppWindow())
            {
                Ui.Setup(dialog, caption, 480, 280); dialog.MinimizeBox = false; dialog.CanResize = false; dialog.ShowInTaskbar = owner == null;
                var ownerForm = owner as Form; if (ownerForm != null) dialog.TopMost = ownerForm.TopMost;
                var root = Ui.Root(Ui.Fill(), Ui.Auto());
                var message = new ReadOnlyText { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
                    BackColor = Theme.Canvas, ForeColor = Theme.Ink, ScrollBars = ScrollBars.None, Text = text, Font = Theme.Font(10, FontStyle.Regular, text) };
                var actions = Ui.Bar(); actions.FlowDirection = FlowDirection.RightToLeft;
                DialogResult reject = buttons == MessageBoxButtons.YesNo ? DialogResult.No : buttons == MessageBoxButtons.OK ? DialogResult.OK : DialogResult.Cancel;
                DialogResult accept = buttons == MessageBoxButtons.YesNo ? DialogResult.Yes : DialogResult.OK;
                var ok = Ui.Button(accept == DialogResult.Yes ? "是" : "确定", delegate { dialog.DialogResult = accept; dialog.Close(); });
                ok.BackColor = Theme.RoseSoft; ok.ForeColor = Theme.Ink; actions.Controls.Add(ok);
                Button cancel = null;
                if (buttons != MessageBoxButtons.OK)
                {
                    cancel = Ui.Button(reject == DialogResult.No ? "否" : "取消", delegate { dialog.DialogResult = reject; dialog.Close(); }); actions.Controls.Add(cancel);
                }
                dialog.AcceptButton = defaultButton == MessageBoxDefaultButton.Button2 && cancel != null ? cancel : ok;
                dialog.CancelButton = cancel ?? ok;
                dialog.FormClosing += delegate { if (dialog.DialogResult == DialogResult.None) dialog.DialogResult = reject; };
                root.Controls.Add(message, 0, 0); root.Controls.Add(actions, 0, 1); dialog.Controls.Add(root);
                dialog.Shown += delegate { Ui.Fit(dialog); ((Control)dialog.AcceptButton).Focus(); };
                return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            }
        }
    }

}
