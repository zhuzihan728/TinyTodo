using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TinyTodo
{
    internal static class Theme
    {
        // Rose Latte. Opaque colors keep GDI and GDI+ surfaces consistent.
        internal static Color Mix(Color foreground, Color background, float amount)
        { return Color.FromArgb((int)(foreground.R * amount + background.R * (1 - amount)), (int)(foreground.G * amount + background.G * (1 - amount)), (int)(foreground.B * amount + background.B * (1 - amount))); }
        internal static readonly Color PaletteRose = ColorTranslator.FromHtml("#E2A5AD");
        internal static readonly Color PaletteBlush = ColorTranslator.FromHtml("#EDCDCE");
        internal static readonly Color PaletteCloud = ColorTranslator.FromHtml("#FAFAF9");
        internal static readonly Color PaletteBlue = ColorTranslator.FromHtml("#B5C7C9");
        internal static readonly Color PaletteTaupe = ColorTranslator.FromHtml("#C9C0B5");
        internal static readonly Color Canvas = PaletteCloud;
        internal static readonly Color Ink = ColorTranslator.FromHtml("#6B3E31");
        internal static readonly Color Latte = ColorTranslator.FromHtml("#926546");
        internal static readonly Color Muted = ColorTranslator.FromHtml("#96766B");
        internal static readonly Color Rose = ColorTranslator.FromHtml("#B85F7A");
        internal static readonly Color RoseSoft = ColorTranslator.FromHtml("#EFCED7");
        internal static readonly Color Teal = ColorTranslator.FromHtml("#427D89");
        internal static readonly Color TealSoft = ColorTranslator.FromHtml("#D8E8EA");
        internal static readonly Color HeaderTint = ColorTranslator.FromHtml("#E8E0D8");
        internal static readonly Color TaskTint = Color.White;
        internal static readonly Color AddAction = ColorTranslator.FromHtml("#DEA9B7");
        internal static readonly Color Chrome = Color.White;
        internal static readonly Color Quote = Latte;
        internal static readonly Color QuoteSoft = ColorTranslator.FromHtml("#F0E5DB");
        internal static readonly Color Border = ColorTranslator.FromHtml("#E6D7CC");
        internal static readonly Color Done = ColorTranslator.FromHtml("#F2EFEC");
        internal static void TaskCard(Graphics g, RectangleF bounds, bool done, bool selected, float scale)
        {
            using (var path = Rounded(bounds, 8 * scale))
            using (var fill = new SolidBrush(done ? Done : TaskTint))
            using (var pen = new Pen(selected ? Latte : done ? Done : TaskTint, 1.6F * scale))
            { g.FillPath(fill, path); if (selected) g.DrawPath(pen, path); }
        }
        private static readonly PrivateFontCollection privateFonts = new PrivateFontCollection();
        private static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        private static readonly HashSet<char> coverage = new HashSet<char>();
        private static FontFamily custom;
        private static Image logo, floating;
        private static bool initialized;
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int AddFontResourceEx(string name, uint flags, IntPtr reserved);
        internal static string Asset(string name)
        { return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "assets", name)); }
        private static void Initialize()
        {
            if (initialized) return; initialized = true;
            try
            {
                string file = Asset("WenYuanRoundedSC-Regular.ttf");
                if (!File.Exists(file)) return;
                privateFonts.AddFontFile(file); AddFontResourceEx(file, 0x10, IntPtr.Zero);
                string bold = Asset("WenYuanRoundedSC-Bold.ttf");
                if (File.Exists(bold)) { privateFonts.AddFontFile(bold); AddFontResourceEx(bold, 0x10, IntPtr.Zero); }
                custom = privateFonts.Families.FirstOrDefault();
                foreach (char c in File.ReadAllText(Asset("Font-coverage.txt"))) coverage.Add(c);
            }
            catch (ArgumentException) { custom = null; }
            catch (IOException) { custom = null; }
            catch (UnauthorizedAccessException) { custom = null; }
        }
        internal static Font Font(float size, FontStyle style, string text, GraphicsUnit unit = GraphicsUnit.Point)
        {
            Initialize(); bool useCustom = custom != null && custom.IsStyleAvailable(style) && (text ?? "").All(c => Char.IsWhiteSpace(c) || coverage.Contains(c));
            string key = (useCustom ? "custom" : "system") + size + style + unit; Font result;
            if (!fonts.TryGetValue(key, out result))
            {
                result = useCustom ? new Font(custom, size, style, unit) : new Font("Microsoft YaHei UI", size, style, unit);
                fonts.Add(key, result);
            }
            return result;
        }
        private static Icon appIcon;
        internal static Icon AppIcon
        {
            get
            {
                if (appIcon == null && File.Exists(Asset("app.ico")))
                    using (var source = new Icon(Asset("app.ico"), Ui.U(32), Ui.U(32))) appIcon = (Icon)source.Clone();
                return appIcon;
            }
        }
        internal static Image Logo
        {
            get
            {
                if (logo == null && File.Exists(Asset("app-icon.png")))
                    logo = LoadIcon("app-icon.png", Ui.U(34));
                return logo;
            }
        }
        internal static Image FloatingLogo
        {
            get
            {
                if (floating == null && File.Exists(Asset("floating-icon.png")))
                    floating = LoadIcon("floating-icon.png", Ui.U(36));
                return floating ?? Logo;
            }
        }
        internal static Bitmap[] LoadFloatingFrames(int size, out int[] delays)
        {
            string file = Asset("floating-icon.gif");
            if (!File.Exists(file)) { delays = new int[] { 100 }; return new Bitmap[] { new Bitmap(FloatingLogo) }; }
            var originals = new List<Bitmap>();
            var frames = new List<Bitmap>();
            try
            {
                using (var animation = Image.FromFile(file))
                {
                    int count = animation.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time);
                    delays = new int[count]; byte[] timing = null;
                    if (animation.PropertyIdList.Contains(0x5100)) timing = animation.GetPropertyItem(0x5100).Value;
                    Rectangle bounds = Rectangle.Empty;
                    for (int i = 0; i < count; i++)
                    {
                        animation.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Time, i);
                        var frame = new Bitmap(animation.Width, animation.Height);
                        using (var g = Graphics.FromImage(frame))
                        { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(animation, 0, 0); }
                        originals.Add(frame);
                        // One shared visible rectangle prevents animation from moving or changing size.
                        int left = frame.Width, top = frame.Height, right = -1, bottom = -1;
                        for (int y = 0; y < frame.Height; y++) for (int x = 0; x < frame.Width; x++)
                        {
                            if (frame.GetPixel(x, y).A == 0) continue;
                            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                        }
                        if (right >= left)
                        {
                            var visible = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
                            bounds = bounds.IsEmpty ? visible : Rectangle.Union(bounds, visible);
                        }
                        delays[i] = timing != null && timing.Length >= (i + 1) * 4 ?
                            Math.Max(20, (int)Math.Min(60000, BitConverter.ToUInt32(timing, i * 4) * 10L)) : 100;
                    }
                    if (bounds.IsEmpty) bounds = new Rectangle(0, 0, animation.Width, animation.Height);
                    float factor = (float)size / Math.Max(bounds.Width, bounds.Height);
                    var target = new RectangleF((size - bounds.Width * factor) / 2, (size - bounds.Height * factor) / 2,
                        bounds.Width * factor, bounds.Height * factor);
                    foreach (var source in originals)
                    {
                        var frame = new Bitmap(size, size); frames.Add(frame);
                        using (var g = Graphics.FromImage(frame))
                        {
                            g.CompositingMode = CompositingMode.SourceCopy;
                            g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.DrawImage(source, target, bounds, GraphicsUnit.Pixel);
                        }
                    }
                }
                // Trim only bottom rows that are transparent in every rendered frame.
                // Keep one fixed height so the character does not bob as its pose changes.
                int lastVisibleRow = -1;
                foreach (var frame in frames)
                {
                    for (int y = frame.Height - 1; y > lastVisibleRow; y--)
                    {
                        bool visible = false;
                        for (int x = 0; x < frame.Width; x++)
                            if (frame.GetPixel(x, y).A != 0) { visible = true; break; }
                        if (visible) { lastVisibleRow = y; break; }
                    }
                }
                if (lastVisibleRow >= 0 && lastVisibleRow + 1 < size)
                {
                    for (int i = 0; i < frames.Count; i++)
                    {
                        var original = frames[i];
                        frames[i] = original.Clone(new Rectangle(0, 0, original.Width, lastVisibleRow + 1),
                            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        original.Dispose();
                    }
                }
                return frames.ToArray();
            }
            catch { foreach (var frame in frames) frame.Dispose(); throw; }
            finally { foreach (var frame in originals) frame.Dispose(); }
        }
        private static Image LoadIcon(string name, int size)
        { using (var source = Image.FromFile(Asset(name))) return FitIcon(source, size); }
        internal static Bitmap FitIcon(Image source, int size, InterpolationMode interpolation = InterpolationMode.NearestNeighbor, byte minimumAlpha = 1)
        {
            using (var bitmap = new Bitmap(source))
            {
                int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    byte[] row = new byte[bitmap.Width * 4];
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                        for (int x = 0; x < bitmap.Width; x++)
                        {
                            // By default preserve all nontransparent pixels. A supplied threshold
                            // can exclude nearly invisible export residue from layout bounds.
                            if (row[x * 4 + 3] < minimumAlpha) continue;
                            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                        }
                    }
                }
                finally { bitmap.UnlockBits(data); }
                Rectangle content = right < left ? new Rectangle(0, 0, bitmap.Width, bitmap.Height) :
                    Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
                var result = new Bitmap(size, size);
                // Fit the visible pixel artwork edge to edge without stretching its proportions.
                float factor = (float)size / Math.Max(content.Width, content.Height);
                var target = new RectangleF((size - content.Width * factor) / 2, (size - content.Height * factor) / 2,
                    content.Width * factor, content.Height * factor);
                using (var g = Graphics.FromImage(result))
                {
                    g.InterpolationMode = interpolation;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(bitmap, target, content, GraphicsUnit.Pixel);
                }
                return result;
            }
        }
        internal static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height)); var p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        internal static Brush Paper(RectangleF bounds, Color color)
        { return new LinearGradientBrush(bounds, Mix(Color.White, color, .09F), Mix(PaletteTaupe, color, .025F), LinearGradientMode.Vertical); }
        internal static void Check(Graphics g, RectangleF r, bool done)
        {
            using (var path = Rounded(r, 4))
            using (var fill = new SolidBrush(done ? Rose : Color.White))
            using (var pen = new Pen(done ? Rose : Theme.Quote, Math.Max(1F, r.Width / 12)))
            { g.FillPath(fill, path); g.DrawPath(pen, path); }
            if (done) using (var pen = new Pen(Color.White, Math.Max(1.5F, r.Width / 8)))
            { pen.StartCap = pen.EndCap = LineCap.Round; g.DrawLines(pen, new PointF[] { new PointF(r.X + r.Width * .23F, r.Y + r.Height * .5F), new PointF(r.X + r.Width * .43F, r.Y + r.Height * .7F), new PointF(r.X + r.Width * .78F, r.Y + r.Height * .28F) }); }
        }
    }
    internal class SoftButton : Button
    {
        protected bool hovered, pressed;
        internal bool IndexTab, SelectedTab;
        internal SoftButton() { ForeColor = Theme.Ink; DoubleBuffered = true; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Color.White; }
        public override Size GetPreferredSize(Size proposedSize)
        {
            if (!AutoSize) return Size;
            int text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            return new Size(text + Ui.U(IndexTab ? 28 : 20) + (Image == null ? 0 : Image.Width + Ui.U(1)), Math.Max(Ui.U(IndexTab ? 34 : 30), Font.Height + Ui.U(8)));
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (Ui.ExactModifiers(e, Keys.None) && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) { pressed = false; Invalidate(); } base.OnMouseCaptureChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Canvas : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = pressed ? Theme.Mix(Theme.PaletteTaupe, BackColor, .10F) : hovered && BackColor == Color.White ? Theme.Mix(Theme.PaletteTaupe, Color.White, .10F) : BackColor;
            if (IndexTab)
            {
                float top = SelectedTab ? 1 : Ui.U(5), radius = Ui.U(7), right = Width - 1, bottom = Height - 1;
                using (var path = new GraphicsPath())
                {
                    path.AddLine(1, bottom, 1, top + radius);
                    path.AddArc(1, top, radius * 2, radius * 2, 180, 90);
                    path.AddArc(right - radius * 2, top, radius * 2, radius * 2, 270, 90);
                    path.AddLine(right, top + radius, right, bottom);
                    using (var brush = new SolidBrush(SelectedTab ? Theme.Canvas : hovered ? Theme.RoseSoft : Theme.Mix(Theme.RoseSoft, Theme.Canvas, .55F))) e.Graphics.FillPath(brush, path);
                    using (var pen = new Pen(SelectedTab ? Theme.Latte : Theme.Border, SelectedTab ? Ui.U(2) : 1)) e.Graphics.DrawPath(pen, path);
                    if (!SelectedTab) using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, bottom, Width, bottom);
                }
            }
            else
            {
                using (var path = Theme.Rounded(new RectangleF(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), Ui.U(6)))
                using (var brush = Theme.Paper(new RectangleF(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), fill))
                using (var pen = new Pen(pressed ? Theme.Rose : Theme.Border, pressed ? Ui.U(2) : 1))
                { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            }
            Rectangle text = new Rectangle(Ui.U(8), Ui.U(3), Math.Max(0, Width - Ui.U(16)), Math.Max(0, Height - Ui.U(6)));
            if (Image != null)
            {
                // Measure and draw with the same engine so font side bearings cannot shift the group.
                using (var format = new StringFormat(StringFormat.GenericTypographic))
                using (var brush = new SolidBrush(Enabled ? ForeColor : Theme.Muted))
                {
                    format.FormatFlags |= StringFormatFlags.NoWrap;
                    SizeF size = e.Graphics.MeasureString(Text, Font, Int32.MaxValue, format);
                    float iconHeight = Math.Min(Ui.U(13), Font.Height), iconWidth = iconHeight * 7 / 13F, gap = Ui.U(1);
                    float x = (Width - size.Width - gap - iconWidth) / 2F;
                    e.Graphics.DrawString(Text, Font, brush, new PointF(x, (Height - size.Height) / 2F), format);
                    e.Graphics.DrawImage(Image, x + size.Width + gap, (Height - iconHeight) / 2F, iconWidth, iconHeight);
                }
                return;
            }
            if (IndexTab && !SelectedTab) { text.Y += Ui.U(2); text.Height -= Ui.U(2); }
            TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled ? ForeColor : Theme.Muted,
                TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }
}
