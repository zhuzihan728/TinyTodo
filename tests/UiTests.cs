using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using TinyTodo;

internal static class UiTests
{
    private static int count;
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        { yield return child; foreach (Control nested in Descendants(child)) yield return nested; }
    }
    private static void Assert(bool condition, string text)
    { if (!condition) throw new Exception("FAIL: " + text); count++; Console.WriteLine("PASS: " + text); }
    private static void ClickCell(TaskTable grid, int column, int row)
    {
        Rectangle cell = grid.CellBounds(column, row);
        int x = cell.Left + cell.Width / 2, y = cell.Top + cell.Height / 2;
        Mouse(grid, "OnMouseDown", MouseButtons.Left, x, y);
        Mouse(grid, "OnMouseUp", MouseButtons.Left, x, y);
        Application.DoEvents();
    }
    private static void DoubleClickCell(TaskTable grid, int column, int row)
    {
        ClickCell(grid, column, row);
        Rectangle cell = grid.CellBounds(column, row);
        int x = cell.Left + cell.Width / 2, y = cell.Top + cell.Height / 2;
        Mouse(grid, "OnMouseDown", MouseButtons.Left, x, y, 2);
        Mouse(grid, "OnMouseUp", MouseButtons.Left, x, y);
        Application.DoEvents();
    }
    private static void Mouse(Control c, string method, MouseButtons button, int x, int y, int clicks = 1)
    { c.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(c, new object[] { new MouseEventArgs(button, clicks, x, y, 0) }); }
    private static void AssertColumns(TaskTable grid)
    {
        grid.FitColumns();
        Assert(grid.Sizing.Width.Select((w, i) => w >= grid.Sizing.Minimum[i]).All(x => x), "all four columns remain above measured minimum");
        Assert(grid.Sizing.Width.Sum() == grid.AvailableWidth, "columns fit exactly inside viewport");
        Assert(grid.Sizing.Width[0] == grid.Sizing.Minimum[0], "completion stays fixed");
        Assert(grid.Sizing.Width[0] >= TextRenderer.MeasureText("完成", grid.Font).Width + Ui.U(16), "completion header fits text and custom padding");
        Assert(grid.GetColumnDisplayRectangle(3, false).Right == grid.AvailableWidth, "status right edge stays pinned");
    }
    private static void AssertPreviewTabs(TaskViews views)
    {
        string[] labels = { "前置", "后续", "树形" };
        for (int i = 0; i < labels.Length; i++)
        {
            Button tab = Descendants(views).OfType<Button>().Single(x => x.Text == labels[i]);
            Assert(tab.Visible && tab.Parent.ClientRectangle.Contains(tab.Bounds), labels[i] + " tab is fully visible in navigation strip");
            Control strip = tab.Parent;
            Assert(strip.Parent.ClientRectangle.Contains(strip.Bounds), labels[i] + " navigation strip is not clipped by toolbar");
            tab.PerformClick(); Application.DoEvents();
            Assert(views.Mode == i && (i == 0 ? views.First.Visible : i == 1 ? views.Second.Visible : views.Graph.Visible), labels[i] + " opens through its visible tab");
        }
        foreach (Control tool in views.Eye.Parent.Controls)
            Assert(tool.Visible && tool.Parent.ClientRectangle.Contains(tool.Bounds), "tree tool is fully visible");
        Assert(views.Eye.Parent.Parent.ClientRectangle.Contains(views.Eye.Parent.Bounds), "tree tools fit toolbar alongside navigation or in second row");
        views.SelectView(0); Application.DoEvents();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr Send(IntPtr handle, int message, IntPtr w, IntPtr l);
    private static void VerifyHover()
    {
        using (var host = new AppWindow())
        using (var hover = new HoverMarkdown())
        {
            host.ClientSize = new Size(Ui.U(460), Ui.U(300)); host.Show(); Application.DoEvents();
            Point originalPointer = Cursor.Position;
            try
            {
                var task = new Todo { Name = "可选择复制的标题", Description = String.Join("\n", Enumerable.Range(1, 60).Select(i => "第 " + i + " 行 Markdown 内容")) };
                hover.Schedule(host, task, host.PointToScreen(new Point(Ui.U(20), Ui.U(45))));
                ((Timer)typeof(HoverMarkdown).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hover)).Stop();
                typeof(HoverMarkdown).GetMethod("Show", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hover, null);
                var card = (Form)typeof(HoverMarkdown).GetField("card", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hover);
                var markdown = Descendants(card).OfType<MarkdownView>().Single();
                host.Activate(); Cursor.Position = markdown.PointToScreen(new Point(Ui.U(30), Ui.U(30))); Application.DoEvents();
                int before = (int)Send(markdown.Handle, 0xCE, IntPtr.Zero, IntPtr.Zero);
                var wheel = Message.Create(host.Handle, 0x20A, new IntPtr(-120 << 16), IntPtr.Zero);
                Assert(((IMessageFilter)card).PreFilterMessage(ref wheel), "hover card consumes wheel addressed to main window");
                int after = (int)Send(markdown.Handle, 0xCE, IntPtr.Zero, IntPtr.Zero);
                Assert(after > before && Form.ActiveForm == host, "hover Markdown scrolls down without taking keyboard focus");
                wheel.WParam = new IntPtr(120 << 16); ((IMessageFilter)card).PreFilterMessage(ref wheel);
                Assert((int)Send(markdown.Handle, 0xCE, IntPtr.Zero, IntPtr.Zero) < after, "hover Markdown scrolls back up");
                markdown.Focus(); markdown.Select(0, 5);
                Assert(markdown.SelectedText.Length == 5 && markdown.ReadOnly && !markdown.HideSelection, "read-only Markdown keeps visible text selection for copying");
                string original = markdown.Text;
                Send(markdown.Handle, 0x102, new IntPtr('x'), IntPtr.Zero);
                Assert(markdown.Text == original, "typing cannot alter read-only Markdown");
                Mouse(markdown, "OnMouseMove", MouseButtons.None, Ui.U(10), Ui.U(10));
                Assert(markdown.Cursor == Cursors.Default, "non-link preview text uses arrow pointer");
                var title = Descendants(card).OfType<ReadOnlyText>().Single(); title.SelectAll();
                Assert(title.SelectedText == task.Name && title.ReadOnly && title.Cursor == Cursors.Default, "hover title is selectable and read-only without editing pointer");
                var viewport = Descendants(card).OfType<TextViewport>().Single(); viewport.Sync();
                int maximum = viewport.Scrollbar.Maximum, page = viewport.Scrollbar.Page;
                viewport.SetPosition(maximum); int bottom = viewport.Position;
                for (int i = 0; i < 10; i++) viewport.ScrollWheel(-120);
                Assert(viewport.Scrollbar.Maximum == maximum && viewport.Scrollbar.Page == page && viewport.Position == bottom, "hover bottom preserves range, thumb length and content position");
                Capture(card, "hover");
                Cursor.Position = new Point(card.Right + 20, card.Bottom + 20);
                Assert(!((IMessageFilter)card).PreFilterMessage(ref wheel), "wheel outside hover card remains with its original target");
                hover.Hide(); Assert(card.IsDisposed, "closing hover releases its message filter and controls");
            }
            finally { Cursor.Position = originalPointer; }
        }
    }
    private static void VerifyStableScrolling()
    {
        using (var host = new AppWindow())
        using (var markdown = new MarkdownView())
        {
            host.ClientSize = new Size(Ui.U(430), Ui.U(290));
            var viewport = new TextViewport(markdown); host.Controls.Add(viewport);
            var other = new Button { Text = "focus", Dock = DockStyle.Top }; host.Controls.Add(other);
            markdown.ShowMarkdown(String.Join("\n", Enumerable.Range(1, 100).Select(i => i % 7 == 0 ? "# 标题 " + i : "第 " + i + " 行 **重点** 内容，包含用于换行的描述文字。")));
            host.Show(); Application.DoEvents(); viewport.Sync();
            int maximum = viewport.Scrollbar.Maximum, page = viewport.Scrollbar.Page, width = markdown.Width;
            Assert(maximum > 0 && page == markdown.ClientSize.Height, "Markdown range uses document pixels and fixed viewport height");
            viewport.SetPosition(maximum); int bottom = viewport.Position;
            for (int i = 0; i < 20; i++) { viewport.SetPosition(Int32.MaxValue); viewport.Sync(); }
            Assert(viewport.Scrollbar.Maximum == maximum && viewport.Scrollbar.Page == page && markdown.Width == width, "bottom scrolling cannot resize thumb or reflow Markdown");
            Assert(viewport.Position == bottom, "repeated bottom scrolling stays at one position");
            int last = markdown.GetCharIndexFromPosition(new Point(markdown.Width - 2, markdown.Height - 2));
            Assert(last >= markdown.Text.IndexOf("第 100 行"), "last Markdown line is reachable at bottom");
            var thumbProperty = typeof(ThinScroll).GetProperty("Thumb", BindingFlags.Instance | BindingFlags.NonPublic);
            Rectangle thumb = (Rectangle)thumbProperty.GetValue(viewport.Scrollbar, null);
            Mouse(viewport.Scrollbar, "OnMouseDown", MouseButtons.Left, thumb.Left + 1, thumb.Top + thumb.Height / 2);
            Mouse(viewport.Scrollbar, "OnMouseMove", MouseButtons.Left, thumb.Left + 1, viewport.Scrollbar.Height + Ui.U(100));
            Mouse(viewport.Scrollbar, "OnMouseUp", MouseButtons.Left, thumb.Left + 1, viewport.Scrollbar.Height + Ui.U(100));
            Rectangle afterThumb = (Rectangle)thumbProperty.GetValue(viewport.Scrollbar, null);
            Assert(afterThumb.Height == thumb.Height && viewport.Scrollbar.Maximum == maximum && viewport.Position == bottom, "dragging beyond bottom keeps thumb length, range and content stable");
            other.Focus(); Point pointer = Cursor.Position;
            try
            {
                Cursor.Position = markdown.PointToScreen(new Point(Ui.U(20), Ui.U(20)));
                var wheel = Message.Create(other.Handle, 0x20A, new IntPtr(120 << 16), IntPtr.Zero);
                Assert(((IMessageFilter)viewport).PreFilterMessage(ref wheel) && viewport.Position < bottom && other.Focused, "preview wheel scrolls under pointer without changing focus");
            }
            finally { Cursor.Position = pointer; }
            viewport.SetPosition(0); Assert(viewport.Position == 0, "Markdown returns to exact top");
            host.Width -= Ui.U(90); Application.DoEvents(); viewport.Sync();
            maximum = viewport.Scrollbar.Maximum; page = viewport.Scrollbar.Page;
            viewport.SetPosition(maximum);
            Assert(viewport.Scrollbar.Maximum == maximum && viewport.Scrollbar.Page == page, "resized Markdown keeps its new range stable at bottom");
            Capture(host, "markdown-bottom");
        }
    }
    private static void VerifyRefinement(Store store)
    {
        using (var main = new MainForm(store, new DataLocations(Path.GetDirectoryName(store.PathName))))
        {
            main.Show(); Application.DoEvents();
            var views = Descendants(main).OfType<TaskViews>().Single(); var grid = views.First;
            var tabs = Descendants(main).OfType<SoftButton>().Where(x => x.Text.StartsWith("待办") || x.Text.StartsWith("历史")).ToArray();
            Assert(tabs.All(x => x.IndexTab) && tabs.Count(x => x.SelectedTab) == 1, "active and history share index tabs with one selected");
            int toggles = 0; grid.ToggleTask = delegate { toggles++; };
            Rectangle cell = grid.CellBounds(0, 0); RectangleF box = grid.CheckBounds(0);
            Mouse(grid, "OnMouseDown", MouseButtons.Left, cell.Left + 2, cell.Top + cell.Height / 2);
            Mouse(grid, "OnMouseUp", MouseButtons.Left, cell.Left + 2, cell.Top + cell.Height / 2);
            Assert(toggles == 0 && grid.SelectedId != null, "click outside checkbox only selects row");
            int checkboxX = (int)(box.X + box.Width / 2), y = (int)(box.Y + box.Height / 2);
            Mouse(grid, "OnMouseDown", MouseButtons.Left, cell.Left + 2, y);
            Mouse(grid, "OnMouseUp", MouseButtons.Left, checkboxX, y);
            Assert(toggles == 0, "press outside and release inside checkbox cannot complete task");
            Mouse(grid, "OnMouseDown", MouseButtons.Left, checkboxX, y);
            Mouse(grid, "OnMouseUp", MouseButtons.Left, checkboxX, y);
            Assert(toggles == 1, "only clicking the actual checkbox completes task");
            views.SelectView(2);
            Control overlay = views.Eye.Parent; Rectangle tools = overlay.Bounds;
            Assert(overlay.Parent == views.Graph && views.Graph.ClientRectangle.Contains(tools), "tree tools overlay top-right inside canvas");
            Mouse(views.Graph, "OnMouseDown", MouseButtons.Left, 20, 20);
            Mouse(views.Graph, "OnMouseMove", MouseButtons.Left, 60, 80);
            Mouse(views.Graph, "OnMouseUp", MouseButtons.Left, 60, 80);
            views.Graph.ZoomAt(1.2F, new Point(100, 100));
            Assert(overlay.Bounds == tools, "canvas pan and zoom never move tree toolbar");
            tabs.First(x => x.Text.StartsWith("历史")).PerformClick(); Application.DoEvents();
            Assert(views.Mode == 0 && grid.Visible && !overlay.Visible, "history tab leaves tree for list");
            views.SelectView(2); tabs.First(x => x.Text.StartsWith("待办")).PerformClick(); Application.DoEvents();
            Assert(views.Mode == 0 && grid.Visible, "active tab leaves tree for list");
            var mode = Descendants(main).OfType<FloatingSwitch>().Single(); Rectangle before = main.Bounds;
            mode.Choose(true); Application.DoEvents();
            Assert(main.Visible && !main.TopMost && main.Bounds == before, "switching open window to floating preserves visibility and bounds");
            mode.Choose(false); Application.DoEvents();
            Assert(main.Visible && !main.TopMost && main.Bounds == before, "switching floating back preserves open window");
            using (var editor = new TaskEditor(store, null))
            {
                editor.Show(); Application.DoEvents();
                Control choices = Descendants(editor).First(c => c.GetType().Name == "TaskChoices");
                Assert((int)choices.GetType().GetField("selected", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(choices) == -1, "new task has no preselected pink prerequisite row");
                editor.Close();
            }
            using (var editor = new TaskEditor(store, store.Current.Tasks.First()))
            {
                editor.Show(); Application.DoEvents();
                Control choices = Descendants(editor).First(c => c.GetType().Name == "TaskChoices");
                Assert((int)choices.GetType().GetField("selected", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(choices) == -1, "editing has no preselected pink prerequisite row");
                editor.Close();
            }
        }
    }
    private static Color Pixel(Control control, int x, int y)
    {
        using (var image = new Bitmap(control.Width, control.Height))
        { control.DrawToBitmap(image, control.ClientRectangle); return image.GetPixel(x, y); }
    }
    private static void VerifySpacingFeedback(MainForm main, Store store)
    {
        var mainViews = Descendants(main).OfType<TaskViews>().Single();
        var mainTab = Descendants(main).OfType<Button>().First(b => b.Text == "列表");
        int mainGap = mainViews.First.PointToScreen(Point.Empty).Y - mainTab.PointToScreen(new Point(0, mainTab.Height)).Y;
        using (var preview = new TaskPreview(store, store.Current.Tasks.First().Id))
        {
            preview.Show(); Application.DoEvents();
            var views = Descendants(preview).OfType<TaskViews>().Single();
            var tab = Descendants(views).OfType<Button>().First(b => b.Text == "前置");
            int previewGap = views.First.PointToScreen(Point.Empty).Y - tab.PointToScreen(new Point(0, tab.Height)).Y;
            Assert(mainGap == Ui.ViewTabGap && previewGap == mainGap, "main and preview have the same three-DIP tab-to-table gap");
            Assert(views.First.PointToScreen(Point.Empty).Y - views.PointToScreen(Point.Empty).Y == tab.Height + Ui.U(3), "preview table keeps its previous vertical position");
            Capture(preview, "preview");
            foreach (TaskTable table in new TaskTable[] { views.First, views.Second })
            {
                views.SelectView(table == views.First ? 0 : 1); Application.DoEvents();
                SoftButton button = table.AddRowButton;
                Assert(button.Visible && button.BackColor == Color.White && button.ForeColor == Theme.Ink, "add relation uses the shared action button colors");
                float gap = Math.Max(1, table.RowHeight * .05F);
                Rectangle expected = Rectangle.Round(new RectangleF(Ui.U(2), table.ColumnHeadersHeight + gap / 2 + Ui.U(1), table.AvailableWidth - Ui.U(4), table.RowHeight - gap - Ui.U(2)));
                Assert(button.Bounds == expected, "add relation retains its row footprint");
                int x = button.Width - Ui.U(20), y = button.Height / 2;
                int clicks = 0; table.AddTask = delegate { clicks++; };
                typeof(SoftButton).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
                Color normal = Pixel(button, x, y);
                typeof(SoftButton).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
                Color hover = Pixel(button, x, y);
                Assert(hover.GetBrightness() < normal.GetBrightness() && button.Cursor == Cursors.Hand, "add relation hover darkens background and shows hand cursor");
                Mouse(button, "OnMouseDown", MouseButtons.Left, x, y);
                Assert(Pixel(button, 1, y).ToArgb() == Theme.Rose.ToArgb(), "add relation press uses the shared action button outline");
                Mouse(button, "OnMouseUp", MouseButtons.Left, x, y);
                clicks = 0; button.PerformClick();
                Assert(clicks == 1, "add relation invokes once");
                typeof(SoftButton).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
                Assert(Pixel(button, 1, y).ToArgb() != Theme.Rose.ToArgb(), "add relation clears pressed outline on release");
                typeof(SoftButton).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
                Assert(Pixel(button, x, y).ToArgb() == normal.ToArgb(), "add relation restores its normal appearance after leaving");
            }
            Assert(preview.Icon == Theme.AppIcon && Theme.AppIcon != null, "task preview uses the personal app icon");
            preview.Size = preview.MinimumSize; Application.DoEvents();
            views.SelectView(0);
            int smallGap = views.First.PointToScreen(Point.Empty).Y - tab.PointToScreen(new Point(0, tab.Height)).Y;
            Assert(smallGap == mainGap, "minimum preview preserves common tab spacing");
        }
    }
    private static void Capture(Control control, string name)
    {
        string folder = Environment.GetEnvironmentVariable("TINYTODO_CAPTURE_DIR");
        if (String.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        using (var image = new Bitmap(control.Width, control.Height))
        {
            control.DrawToBitmap(image, control.ClientRectangle);
            image.Save(Path.Combine(folder, name + ".png"));
        }
    }
    private static void VerifyWindowRecovery(Store store)
    {
        using (var main = new MainForm(store, new DataLocations(Path.GetDirectoryName(store.PathName))))
        {
            main.Show(); Application.DoEvents();
            Descendants(main).OfType<FloatingSwitch>().Single().Choose(true); Application.DoEvents();
            var bubble = (FloatingIcon)typeof(MainForm).GetField("bubble", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(main);
            main.Hide(); main.ToggleFromBubble(); Application.DoEvents();
            Assert(main.Visible && main.WindowState == FormWindowState.Normal, "bubble restores hidden main window");
            main.WindowState = FormWindowState.Minimized;
            main.ToggleFromBubble();
            Application.DoEvents();
            Assert(main.Visible && main.WindowState == FormWindowState.Normal, "bubble restores minimized main window");
            using (var other = new Form())
            {
                other.Show(); other.Activate(); Application.DoEvents(); main.ToggleFromBubble();
                Assert(main.Visible, "bubble reveals an inactive visible main window instead of collapsing it");
                other.Close();
            }
            using (var dialog = new TaskPreview(store, store.Current.Tasks.First().Id))
            {
                Exception failure = null;
                dialog.Shown += delegate { dialog.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        dialog.WindowState = FormWindowState.Minimized;
                        main.ToggleFromBubble();
                        Assert(main.Visible && dialog.Visible && dialog.WindowState == FormWindowState.Normal, "bubble restores modal preview without hiding its disabled owner");
                        main.UpdateDesktopVisibility(true);
                        Assert(!bubble.Visible && !main.TopMost && !dialog.TopMost, "fullscreen removes bubble and main/dialog pinning");
                        IntPtr foreground = DesktopActivity.GetForegroundWindow();
                        main.UpdateDesktopVisibility(false);
                        Assert(bubble.Visible && !main.TopMost && !dialog.TopMost && DesktopActivity.GetForegroundWindow() == foreground, "bubble restoration leaves focus and other windows unchanged");
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { dialog.Close(); }
                })); };
                dialog.ShowDialog(main); if (failure != null) throw failure;
            }
            main.UpdateDesktopVisibility(false);
            Assert(bubble.Visible && !main.TopMost, "floating mode keeps task window in normal window stack");
            Descendants(main).OfType<FloatingSwitch>().Single().Choose(false);
            main.UpdateDesktopVisibility(false);
            Assert(!bubble.Visible && !main.TopMost, "normal window mode remains unpinned with no bubble");
        }
        Assert(DesktopActivity.CoversScreen(new Rectangle(-1920, 0, 1920, 1080), new Rectangle(-1920, 0, 1920, 1080)), "borderless fullscreen detected on negative-coordinate monitor");
        Assert(!DesktopActivity.CoversScreen(new Rectangle(0, 0, 1920, 1040), new Rectangle(0, 0, 1920, 1080)), "working-area window is not mistaken for fullscreen");
        int clicks = 0;
        using (var menu = new ContextMenuStrip())
        using (var bubble = new FloatingIcon(delegate { clicks++; }, delegate(Point point) { }, menu))
        {
            var timer = (System.Windows.Forms.Timer)typeof(FloatingIcon).GetField("animationTimer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bubble);
            var frames = (Bitmap[])typeof(FloatingIcon).GetField("animationFrames", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bubble);
            var frameIndex = typeof(FloatingIcon).GetField("frameIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(frames.Length == 48 && timer.Interval == 100, "floating GIF retains all frames and original timing");
            Assert(bubble.TransparencyKey == bubble.BackColor && bubble.Region == null,
                "floating GIF has transparent window background without a white rounded panel");
            Assert(frames.All(frame => frame.GetPixel(0, 0).A == 0), "floating animation retains transparent frame corners");
            Assert(frames.All(frame => frame.Size == bubble.ClientSize), "floating window matches the trimmed animation bounds");
            Assert(frames.Any(frame => Enumerable.Range(0, frame.Width).Any(x => frame.GetPixel(x, frame.Height - 1).A != 0)),
                "floating animation has no shared transparent bottom rows");
            Assert(!timer.Enabled, "hidden floating animation starts paused");
            string capture = Environment.GetEnvironmentVariable("TINYTODO_CAPTURE_DIR");
            if (!String.IsNullOrEmpty(capture))
            {
                Directory.CreateDirectory(capture);
                frames[0].Save(Path.Combine(capture, "floating-frame-0.png"));
                frames[24].Save(Path.Combine(capture, "floating-frame-24.png"));
            }
            bubble.Show();
            int initialFrame = (int)frameIndex.GetValue(bubble);
            WaitUntil(() => (int)frameIndex.GetValue(bubble) != initialFrame, "visible floating GIF advances frames");
            bubble.Hide();
            int pausedFrame = (int)frameIndex.GetValue(bubble);
            System.Threading.Thread.Sleep(150); Application.DoEvents();
            Assert(!timer.Enabled && (int)frameIndex.GetValue(bubble) == pausedFrame, "hidden floating GIF stops its timer and frame advancement");
            bubble.Show(); Assert(timer.Enabled, "floating GIF resumes when shown");
            Mouse(bubble, "OnMouseDown", MouseButtons.Left, 10, 10);
            Mouse(bubble, "OnMouseUp", MouseButtons.Left, 10, 10);
            Assert(clicks == 0 && !bubble.Capture, "bubble releases mouse before scheduling window activation");
            Application.DoEvents(); Assert(clicks == 1, "bubble click invokes once after mouse release");
            Mouse(bubble, "OnMouseDown", MouseButtons.Left, 10, 10); bubble.Capture = false;
            Mouse(bubble, "OnMouseUp", MouseButtons.Left, 10, 10); Application.DoEvents();
            Assert(clicks == 1, "cancelled mouse capture does not trigger stale bubble click");
        }
    }
    private static void VerifyTrayMenu(Store store)
    {
        using (var main = new MainForm(store, new DataLocations(Path.GetDirectoryName(store.PathName))))
        {
            main.Show(); Application.DoEvents();
            var tray = (NotifyIcon)typeof(MainForm).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(main);
            var menu = (ContextMenuStrip)typeof(MainForm).GetField("menu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(main);
            var mouseUp = typeof(NotifyIcon).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (bool floating in new bool[] { false, true })
            {
                Descendants(main).OfType<FloatingSwitch>().Single().Choose(floating);
                foreach (bool hidden in new bool[] { false, true })
                {
                    if (hidden) main.Hide(); else { main.Show(); main.Activate(); }
                    Application.DoEvents();
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        string closeReason = "none";
                        ToolStripDropDownClosedEventHandler closed = delegate(object sender, ToolStripDropDownClosedEventArgs e) { closeReason = e.CloseReason.ToString(); };
                        menu.Closed += closed;
                        main.UpdateDesktopVisibility(false);
                        bool wasVisible = main.Visible, wasPinned = main.TopMost;
                        mouseUp.Invoke(tray, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 0, 0, 0) });
                        Assert(!menu.Visible, "tray menu waits until right-button release message finishes");
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        while (watch.ElapsedMilliseconds < 850) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                        Assert(main.Visible == wasVisible && main.TopMost == wasPinned, "opening tray menu does not show or repin main window");
                        Assert(menu.Visible, "tray menu survives timer ticks: floating=" + floating + ", hidden=" + hidden + ", attempt=" + attempt + ", closed=" + closeReason);
                        menu.Close(ToolStripDropDownCloseReason.Keyboard); Application.DoEvents();
                        Assert(!menu.Visible && !menu.IsDisposed, "tray menu dismisses normally and can reopen");
                        menu.Closed -= closed;
                    }
                }
            }
        }
    }
    private static void WaitUntil(Func<bool> condition, string message)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 3000) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        Assert(condition(), message);
    }
    private static void VerifyCompletionFeedback(string folder)
    {
        var store = new Store(Path.Combine(folder, "feedback.json")); store.Load();
        var first = new Todo { Name = "First" }; var second = new Todo { Name = "Second" }; var third = new Todo { Name = "Blocked" };
        third.Prerequisites.Add(second.Id);
        store.Change(s => s.Tasks.AddRange(new Todo[] { first, second, third }));
        using (var main = new MainForm(store, new DataLocations(folder)))
        {
            main.Show(); Application.DoEvents(); var table = Descendants(main).OfType<TaskTable>().Single();
            int blockedRow = table.Rows.FindIndex(r => r.Task.Id == third.Id);
            using (var dismiss = new Timer { Interval = 40 })
            {
                dismiss.Tick += delegate
                {
                    foreach (Form form in Application.OpenForms.Cast<Form>().Where(f => f.Owner == main && f.Modal).ToArray()) form.Close();
                };
                dismiss.Start(); ClickCell(table, 0, blockedRow); dismiss.Stop();
            }
            Assert(!Rules.Get(store.Current, third.Id).Done && !table.HasCompletionFeedback(third.Id) && table.Rows.Count == 3,
                "blocked completion keeps task unchanged without a success effect");
            ClickCell(table, 0, table.Rows.FindIndex(r => r.Task.Id == first.Id));
            ClickCell(table, 0, table.Rows.FindIndex(r => r.Task.Id == second.Id));
            Assert(table.HasCompletionFeedback(first.Id) && table.HasCompletionFeedback(second.Id) && table.Rows.Count == 3,
                "different tasks can finish together with independent feedback");
            // Hold the pointer over an exiting row while the rows beneath it change position.
            Rectangle cell = table.CellBounds(0, table.Rows.FindIndex(r => r.Task.Id == second.Id));
            Mouse(table, "OnMouseDown", MouseButtons.Left, cell.Left + cell.Width / 2, cell.Top + cell.Height / 2);
            WaitUntil(() => !table.HasCompletionFeedback(first.Id) && !table.HasCompletionFeedback(second.Id), "concurrent feedback finishes");
            Mouse(table, "OnMouseUp", MouseButtons.Left, cell.Left + cell.Width / 2, cell.Top + cell.Height / 2); Application.DoEvents();
            Assert(table.Rows.Count == 1 && !Rules.Get(store.Current, third.Id).Done,
                "row removal during a mouse press cannot complete the next task");
            Descendants(main).OfType<Button>().First(b => b.Text.StartsWith("历史")).PerformClick();
            ClickCell(table, 0, table.Rows.FindIndex(r => r.Task.Id == second.Id));
            Descendants(main).OfType<Button>().First(b => b.Text.StartsWith("待办")).PerformClick(); Application.DoEvents();
            Assert(!table.HasCompletionFeedback(second.Id) && table.Rows.Count == 2 && table.Rows.All(r => !r.Task.Done),
                "changing tabs clears outgoing rows and immediately shows the correct tasks");
        }
    }
    private static KeyEventArgs Key(Control target, Keys keys)
    {
        var e = new KeyEventArgs(keys);
        target.GetType().GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, new object[] { e });
        return e;
    }
    private static void VerifyInputIsolation(Store store)
    {
        using (var main = new MainForm(store, new DataLocations(Path.GetDirectoryName(store.PathName))))
        using (var editor = new TaskEditor(store, null))
        {
            main.Show(); Application.DoEvents();
            var views = Descendants(main).OfType<TaskViews>().Single();
            var mode = Descendants(main).OfType<FloatingSwitch>().Single();
            int toggles = 0, opens = 0;
            views.First.ToggleTask = delegate { toggles++; }; views.First.OpenTask = delegate { opens++; };
            views.First.AddTask = delegate { opens++; }; views.Graph.OpenTask = delegate { opens++; };
            Key(views.First, Keys.Home);
            Control choices = Descendants(editor).First(c => c.GetType().Name == "TaskChoices");
            foreach (Keys keys in new Keys[] { Keys.Control | Keys.Space, Keys.Control | Keys.Shift | Keys.Space,
                Keys.Alt | Keys.Space, Keys.Alt | Keys.Shift | Keys.Space, Keys.Control | Keys.ShiftKey,
                Keys.Alt | Keys.ShiftKey, Keys.ShiftKey, Keys.LWin, Keys.RWin })
            {
                bool floating = mode.IsFloating; float zoom = views.Graph.Zoom;
                foreach (Control target in new Control[] { main, views.First, views.Graph, mode, choices })
                {
                    var e = Key(target, keys);
                    Assert(!e.SuppressKeyPress && !e.Handled, target.GetType().Name + " leaves input-switch chord untouched: " + keys);
                }
                Assert(mode.IsFloating == floating && views.Graph.Zoom == zoom && toggles == 0 && opens == 0,
                    "input-switch chord cannot toggle tasks, open dialogs or change floating mode");
            }
            Assert(Key(views.First, Keys.Space).SuppressKeyPress && toggles == 1, "plain Space still completes a selected task");
            using (var menu = new ContextMenuStrip())
            using (var bubble = new FloatingIcon(delegate { }, delegate(Point point) { }, menu))
            {
                bubble.Show();
                Assert(Send(bubble.Handle, 0x21, main.Handle, new IntPtr(0x02010001)).ToInt32() == 3,
                    "floating icon refuses mouse activation without discarding click");
            }
        }
        // A separate process exercises the real external-foreground detection path.
        store.Change(s => s.Window.Floating = true);
        using (var main = new MainForm(store, new DataLocations(Path.GetDirectoryName(store.PathName))))
        {
            main.Show(); Application.DoEvents();
            var startupBubble = (FloatingIcon)typeof(MainForm).GetField("bubble", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(main);
            Assert(main.Visible && startupBubble.Visible && !main.TopMost, "floating startup shows both task window and floating entry without pinning");
            string ready = Path.Combine(Path.GetDirectoryName(store.PathName), "input-probe-ready.txt");
            using (var probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath,
                "--input-probe \"" + ready + "\"") { UseShellExecute = false, CreateNoWindow = true }))
            {
                IntPtr foreground = IntPtr.Zero;
                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (!File.Exists(ready) && !probe.HasExited && watch.ElapsedMilliseconds < 5000)
                    { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    Assert(File.Exists(ready), "external input probe started");
                    foreground = new IntPtr(Int64.Parse(File.ReadAllText(ready)));
                    watch.Restart();
                    while (DesktopActivity.GetForegroundWindow() != foreground && watch.ElapsedMilliseconds < 1500)
                    { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    Assert(DesktopActivity.GetForegroundWindow() == foreground && !DesktopActivity.OwnsForeground,
                        "test input window belongs to another foreground process");
                    uint processId; uint threadId = GetWindowThreadProcessId(foreground, out processId);
                    IntPtr layout = GetKeyboardLayout(threadId);
                    main.UpdateDesktopVisibility(true); main.UpdateDesktopVisibility(false);
                    watch.Restart(); bool stable = true;
                    while (watch.ElapsedMilliseconds < 1300)
                    {
                        Application.DoEvents(); System.Threading.Thread.Sleep(10);
                        stable &= DesktopActivity.GetForegroundWindow() == foreground && GetKeyboardLayout(threadId) == layout;
                    }
                    Assert(stable && main.Visible && !main.TopMost,
                        "background ticks and floating restoration preserve external foreground and keyboard layout");
                }
                finally
                {
                    if (foreground != IntPtr.Zero) Program.PostMessage(foreground, 0x10, IntPtr.Zero, IntPtr.Zero);
                    if (!probe.WaitForExit(3000)) probe.Kill();
                    if (File.Exists(ready)) File.Delete(ready);
                }
            }
        }
        store.Change(s => s.Window.Floating = false);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 2 && args[0] == "--input-probe")
        {
            using (var probe = new Form { Text = "TinyTodo isolated input test", ClientSize = new Size(320, 100) })
            {
                var edit = new TextBox { Dock = DockStyle.Fill, Multiline = true }; probe.Controls.Add(edit);
                probe.Shown += delegate { edit.Focus(); File.WriteAllText(args[1], probe.Handle.ToInt64().ToString()); };
                Application.Run(probe); return 0;
            }
        }
        string temp = Path.Combine(Path.GetTempPath(), "TinyTodo-ui-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Stress the actual layout arithmetic used by mouse dragging, with changing panel sizes.
            var widths = new ColumnWidths(44, 88, 108, 96); var random = new Random(304);
            bool invariant = true;
            for (int i = 0; i < 20000; i++)
            {
                int total = widths.MinTotal + random.Next(0, 1000); widths.Fit(total);
                widths.Drag(random.Next(1, 3), random.Next(-4000, 4001));
                invariant &= widths.Width.Sum() == total && widths.Width[0] == 44 && widths.Width.Select((w, col) => w >= widths.Minimum[col]).All(x => x);
            }
            Assert(invariant, "20000 resize/drag operations preserve total, fixed tick and every minimum");
            widths.Fit(widths.MinTotal); Assert(widths.Width.SequenceEqual(widths.Minimum), "minimum panel width naturally resets columns");

            var locations = new DataLocations(temp); locations.Load(); var store = new Store(locations.TaskPath); store.Load();
            var a = new Todo { Name = "前置 A：中文长名称在列尾显示省略号，同时可双击预览完整任务", Due = "2026-09-30", Description = "# 今日计划\n- **关键**事项\n`demo`" };
            var b = new Todo { Name = "后续 B" }; b.Prerequisites.Add(a.Id);
            var c = new Todo { Name = "间接后续 C" }; c.Prerequisites.Add(b.Id);
            var d = new Todo { Name = "共享后续 D" }; d.Prerequisites.Add(a.Id); d.Prerequisites.Add(b.Id);
            store.Change(s => s.Tasks.AddRange(new Todo[] { a, b, c, d }));
            if (Environment.GetEnvironmentVariable("TINYTODO_INPUT_ONLY") == "1") { VerifyInputIsolation(store); return 0; }
            if (Environment.GetEnvironmentVariable("TINYTODO_TRAY_ONLY") == "1") { VerifyTrayMenu(store); return 0; }
            if (Environment.GetEnvironmentVariable("TINYTODO_WINDOW_ONLY") == "1") { VerifyWindowRecovery(store); return 0; }
            using (var main = new MainForm(store, locations))
            {
                main.Show(); Application.DoEvents();
                Assert(main.FormBorderStyle == FormBorderStyle.None && main.Padding.Top == main.CaptionHeight, "custom caption reserves its own client area");
                Assert(main.EdgeHit(new Point(1, main.Height / 2)) == 10 && main.EdgeHit(new Point(main.Width - 1, main.Height - 1)) == 17, "borderless resize hit zones preserve edges and corners");
                Assert(Descendants(main).OfType<Button>().Any(x => x.AccessibleName == "关闭") && Descendants(main).OfType<Button>().Any(x => x.AccessibleName == "最小化"), "integrated caption buttons available");
                Assert(main.EdgeHit(new Point(Ui.U(12), Ui.U(12))) == 13 && main.EdgeHit(new Point(main.Width - Ui.U(12), main.Height - Ui.U(12))) == 17, "wide corner zones support diagonal resizing inside rounded edge");
                var floatingMode = Descendants(main).OfType<FloatingSwitch>().Single();
                Assert(floatingMode.Top < main.CaptionHeight, "floating mode capsule is in caption");
                var mainTabs = Descendants(main).OfType<MainToolbar>().Single();
                var listTab = Descendants(mainTabs).OfType<Button>().Single(x => x.Text == "列表");
                var addButton = Descendants(mainTabs).OfType<Button>().Single(x => x.AccessibleName == "新增");
                var inactiveTab = Descendants(mainTabs).OfType<SoftButton>().First(x => x.IndexTab && !x.SelectedTab);
                float inactiveCenter = inactiveTab.PointToScreen(Point.Empty).Y + (Ui.U(5) + inactiveTab.Height - 1) / 2F;
                Assert(Math.Abs(inactiveCenter - addButton.PointToScreen(Point.Empty).Y - addButton.Height / 2F) <= 1 && addButton.Height <= inactiveTab.Height - Ui.U(5), "add circle aligns with inactive tab visible center and fits its height");
                Capture(main, "main");
                if (Environment.GetEnvironmentVariable("TINYTODO_LAYOUT_ONLY") == "1") { VerifySpacingFeedback(main, store); Console.WriteLine("Layout and relation feedback checks passed."); return 0; }
                var grid = Descendants(main).OfType<TaskTable>().First();
                var views = Descendants(main).OfType<TaskViews>().First();
                Assert(!views.Eye.Visible, "main list hides eye");
                var zonePicker = Descendants(main).OfType<ZoneSelector>().Single(x => x.AccessibleName == "时区");
                Assert(zonePicker.ForeColor == Theme.Ink && zonePicker.Width <= TextRenderer.MeasureText("系统时区", zonePicker.Font, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.U(7), "system zone chip uses brown text and compact side padding");
                Assert(zonePicker.SelectedItem == zonePicker.Items[0] && zonePicker.Items[1].ToString().Contains("AoE") && zonePicker.Items[2].ToString().Contains("英国") && zonePicker.Items[3].ToString().Contains("中国"), "clock follows system by default and pins requested zones first");
                zonePicker.Commit(zonePicker.Items[1]);
                Assert(store.Current.Window.ClockZoneId == WorldClock.AoE && Descendants(main).OfType<Label>().Any(x => x.AccessibleName == "当前日期和时间" && x.Text.EndsWith("UTC-12:00")), "selecting AoE saves preference and updates clock offset");
                zonePicker.Commit(zonePicker.Items[0]);
                store.Change(s => Rules.Get(s, b.Id).Due = "2026-09-20");
                ClickCell(grid, 2, -1);
                Assert(store.Current.Window.DateDescending && grid.Rows[0].Task.Id == a.Id && grid.Rows.Skip(2).All(x => Rules.Get(store.Current, x.Task.Id).Due == null), "date header sorts descending with undated last");
                ClickCell(grid, 2, -1);
                Assert(!store.Current.Window.DateDescending && grid.Rows[0].Task.Id == b.Id, "date header toggles ascending");
                grid.ToggleDateMode();
                Assert(store.Current.Window.DateCountdown && grid.DateHeader.StartsWith("倒计时") && grid.Rows[0].DateText == Rules.Countdown("2026-09-20", DateTime.Today), "date display toggles to countdown");
                grid.ToggleDateMode(); store.Change(s => Rules.Get(s, b.Id).Due = null);
                Descendants(main).OfType<Button>().First(x => x.Text.StartsWith("待办")).PerformClick();
                Assert(Descendants(main).OfType<Button>().Any(x => x.AccessibleName == "新增"), "main add action has accessible label");
                AssertColumns(grid);
                for (int col = 1; col <= 2; col++)
                {
                    int x = grid.Sizing.Width.Take(col + 1).Sum();
                    Mouse(grid, "OnMouseDown", MouseButtons.Left, x, 10);
                    Mouse(grid, "OnMouseMove", MouseButtons.Left, x + 5000, 10);
                    Mouse(grid, "OnMouseUp", MouseButtons.Left, x + 5000, 10); AssertColumns(grid);
                    x = grid.Sizing.Width.Take(col + 1).Sum();
                    Mouse(grid, "OnMouseDown", MouseButtons.Left, x, 10);
                    Mouse(grid, "OnMouseMove", MouseButtons.Left, x - 5000, 10);
                    Mouse(grid, "OnMouseUp", MouseButtons.Left, x - 5000, 10); AssertColumns(grid);
                }
                main.Size = main.MinimumSize; Application.DoEvents(); AssertColumns(grid);
                Assert(grid.Sizing.Width[2] == grid.Sizing.Minimum[2] && grid.Sizing.Width[3] == grid.Sizing.Minimum[3], "shrinking window resets date and status to minimum");
                main.ClientSize = new Size(Ui.U(720), Ui.U(500)); Application.DoEvents(); AssertColumns(grid);
                Assert(!typeof(DataGridView).IsAssignableFrom(grid.GetType()), "task list has no native grid surface");
                Assert(Theme.Canvas.ToArgb() == ColorTranslator.FromHtml("#FAFAF9").ToArgb(), "main canvas uses near-neutral white");
                Assert(floatingMode.BackColor == Descendants(main).OfType<IconButton>().First(x => x.Kind == Glyph.Settings).BackColor && floatingMode.BackColor != Theme.Canvas, "mode toggle shares caption button surface");
                Assert(!Descendants(main).OfType<Button>().Any(x => x.Text.Contains("重置") || x.Text.Contains("恢复") || x.Text.Contains("删除") || x.Text.Contains("完成") || x.Text.Contains("详情")), "main has no redundant action buttons");
                int row = grid.Rows.FindIndex(x => x.Task.Id == a.Id);
                int opened = 0; Action<string> originalOpen = grid.OpenTask;
                grid.OpenTask = delegate(string taskId) { Assert(taskId == a.Id, "table opens intended task"); opened++; };
                ClickCell(grid, 1, row);
                Assert(opened == 0 && grid.SelectedId == a.Id, "single click selects without preview");
                DoubleClickCell(grid, 1, row);
                Assert(opened == 1, "double click opens preview once");
                grid.OpenTask = originalOpen;
                var popup = TaskActions.Menu(main, grid, new Point(20, 20), store, a.Id, delegate { });
                foreach (var reason in new ToolStripDropDownCloseReason[] { ToolStripDropDownCloseReason.AppClicked, ToolStripDropDownCloseReason.Keyboard, ToolStripDropDownCloseReason.AppFocusChange })
                {
                    popup.Close(reason); Application.DoEvents();
                    Assert(!popup.Visible && !popup.IsDisposed, "cancel dismisses menu without destroying it: " + reason);
                    Assert(Object.ReferenceEquals(popup, TaskActions.Menu(main, grid, new Point(20, 20), store, b.Id, delegate { })), "reopening reuses source menu");
                }
                popup.Close(); Application.DoEvents();
                Assert(store.Current.Tasks.Count == 4 && !Rules.Get(store.Current, a.Id).Done, "menu cancellation leaves data unchanged");
                popup = TaskActions.Menu(main, grid, new Point(20, 20), store, a.Id, delegate { Descendants(main).OfType<Button>().First(x => x.Text.StartsWith("待办")).PerformClick(); });
                bool editorSeen = false, editorCorrect = false, editorMarkdown = false;
                using (var cancelEditor = new System.Windows.Forms.Timer { Interval = 30 })
                {
                    cancelEditor.Tick += delegate
                    {
                        var editor = Application.OpenForms.Cast<Form>().OfType<TaskEditor>().FirstOrDefault();
                        if (editor == null) return;
                        cancelEditor.Stop(); editorSeen = true;
                        editorCorrect = Descendants(editor).OfType<TextBox>().Any(x => x.MaxLength == 120 && x.Text == a.Name);
                        var tabs = Descendants(editor).OfType<MarkdownEditor>().Single(); tabs.SelectPreview(true);
                        editorMarkdown = Descendants(editor).OfType<MarkdownView>().Any(x => x.Text.Contains("关键") && !x.Text.Contains("**"));
                        editor.DialogResult = DialogResult.Cancel; editor.Close();
                    };
                    cancelEditor.Start(); popup.Items.OfType<ToolStripMenuItem>().First(x => x.Text == "编辑").PerformClick(); popup.Close(); Application.DoEvents();
                }
                Assert(editorSeen && editorCorrect && editorMarkdown && Rules.Get(store.Current, a.Id).Description == a.Description, "popup edit opens correct task, renders Markdown and cancel leaves source intact");
                popup = TaskActions.Menu(main, grid, new Point(20, 20), store, a.Id, delegate { Descendants(main).OfType<Button>().First(x => x.Text.StartsWith("待办")).PerformClick(); });
                var flagAction = popup.Items.OfType<ToolStripMenuItem>().First(x => x.Text == "设为！");
                Assert(flagAction.Image == null && flagAction.Tag == null, "important menu is plain text without a separate icon");
                flagAction.PerformClick(); popup.Close(); Application.DoEvents();
                Assert(Rules.Get(store.Current, a.Id).Important && grid.Rows[0].Task.Name == a.Name && grid.Rows[0].Task.Important, "menu important action persists flag and decorates unchanged task name");
                popup = TaskActions.Menu(main, grid, new Point(20, 20), store, a.Id, delegate { });
                Assert(popup.Items.OfType<ToolStripMenuItem>().Any(x => x.Text == "取消！" && x.Image == null && x.Tag == null), "flagged menu keeps plain cancel label"); popup.Close();
                using (var source = new Panel())
                {
                    main.Controls.Add(source);
                    var ownedPopup = TaskActions.Menu(main, source, Point.Empty, store, a.Id, delegate { });
                    ownedPopup.Close(); source.Dispose();
                    Assert(ownedPopup.IsDisposed, "source disposal releases its menu");
                }
                ClickCell(grid, 0, row);
                Assert(Rules.Get(store.Current, a.Id).Done && grid.Rows.Count == 4 && grid.HasCompletionFeedback(a.Id), "tick saves immediately and retains checked row for feedback");
                var saved = new Store(store.PathName); saved.Load();
                Assert(Rules.Get(saved.Current, a.Id).Done, "completed state is durable before visual feedback finishes");
                Capture(main, "completion-feedback");
                ClickCell(grid, 0, row);
                Assert(Rules.Get(store.Current, a.Id).Done, "repeat click cannot undo an outgoing row during feedback");
                WaitUntil(() => !grid.HasCompletionFeedback(a.Id), "completion fade finishes");
                Assert(grid.Rows.Count == 3 && grid.Rows.All(r => r.Task.Id != a.Id), "completed row leaves active list after feedback");
                Descendants(main).OfType<Button>().First(x => x.Text.StartsWith("历史")).PerformClick(); Application.DoEvents();
                Assert(grid.Rows.Count == 1 && grid.Rows[0].Task.Id == a.Id, "history contains completed task");
                Assert(!views.Eye.Visible, "history list hides eye");
                views.SelectView(2);
                Assert(views.ShowDone && views.Graph.LayoutData.Nodes.ContainsKey(a.Id), "history tree defaults to completed tasks visible");
                views.SetShowDone(false); views.SelectView(0); views.SelectView(2);
                Assert(views.ShowDone, "reentering history tree restores open-eye default");
                Descendants(main).OfType<Button>().First(x => x.Text.StartsWith("待办")).PerformClick();
                Assert(!views.ShowDone && !views.Graph.LayoutData.Nodes.ContainsKey(a.Id), "switching to active tree closes eye");
                Descendants(main).OfType<Button>().First(x => x.Text.StartsWith("历史")).PerformClick(); views.SelectView(0);
                ClickCell(grid, 0, 0);
                Assert(!Rules.Get(store.Current, a.Id).Done && grid.Rows.Count == 1 && !grid.Rows[0].Task.Done && grid.HasCompletionFeedback(a.Id), "untick saves restoration and shows unchecked history row during feedback");
                Capture(main, "restore-feedback");
                WaitUntil(() => !grid.HasCompletionFeedback(a.Id), "restoration fade finishes");
                Assert(grid.Rows.Count == 0, "restored row leaves history only after feedback");
                views.SelectView(2); views.SetShowDone(true);
                Assert(views.Eye.Visible && views.Eye.Parent == Descendants(views).OfType<TrackButton>().First().Parent, "tree eye shares zoom and track toolbar");
                Assert(views.Graph.LayoutData.Nodes.Count == 4 && views.Graph.LayoutData.Edges.Count == 4, "unified graph retains diamond edges without duplicate nodes");
                var graph = views.Graph;
                Point anchor = new Point(graph.Width / 2, graph.Height / 2);
                var aRect = graph.LayoutData.Nodes[a.Id].Bounds;
                int graphOpened = 0, graphToggled = 0;
                Action<string> graphOpen = graph.OpenTask, graphToggle = graph.ToggleTask;
                graph.OpenTask = delegate(string taskId) { graphOpened++; };
                graph.ToggleTask = delegate(string taskId) { graphToggled++; };
                Point nodePoint = Point.Round(graph.ToScreen(new PointF(aRect.X + 100, aRect.Y + 20)));
                Mouse(graph, "OnMouseDown", MouseButtons.Left, nodePoint.X, nodePoint.Y);
                Mouse(graph, "OnMouseUp", MouseButtons.Left, nodePoint.X, nodePoint.Y);
                Assert(graphOpened == 0 && graph.SelectedId == a.Id, "graph single click selects only");
                Mouse(graph, "OnMouseDown", MouseButtons.Left, nodePoint.X, nodePoint.Y, 2);
                Mouse(graph, "OnMouseUp", MouseButtons.Left, nodePoint.X, nodePoint.Y);
                Assert(graphOpened == 1, "graph double click opens once on release");
                Point tickPoint = Point.Round(graph.ToScreen(new PointF(aRect.X + 19, aRect.Y + 21)));
                Mouse(graph, "OnMouseDown", MouseButtons.Left, tickPoint.X, tickPoint.Y);
                Mouse(graph, "OnMouseUp", MouseButtons.Left, tickPoint.X, tickPoint.Y);
                Mouse(graph, "OnMouseDown", MouseButtons.Left, tickPoint.X, tickPoint.Y, 2);
                Mouse(graph, "OnMouseUp", MouseButtons.Left, tickPoint.X, tickPoint.Y);
                Assert(graphToggled == 1 && graphOpened == 1, "graph tick stays single click and double click never opens preview");
                Mouse(graph, "OnMouseDown", MouseButtons.Left, nodePoint.X, nodePoint.Y, 2);
                Mouse(graph, "OnMouseMove", MouseButtons.Left, nodePoint.X + 30, nodePoint.Y + 20);
                Mouse(graph, "OnMouseUp", MouseButtons.Left, nodePoint.X + 30, nodePoint.Y + 20);
                Assert(graphOpened == 1 && graphToggled == 1, "drag after second press does not open or toggle");
                graph.OpenTask = graphOpen; graph.ToggleTask = graphToggle;
                var beforeZoom = graph.ToScreen(new PointF(aRect.X, aRect.Y));
                graph.ZoomAt(1.5F, anchor);
                var afterZoom = graph.ToScreen(new PointF(aRect.X, aRect.Y));
                Assert(Math.Abs(afterZoom.X - (anchor.X + (beforeZoom.X - anchor.X) * 1.5F)) < .1F, "zoom keeps pointer anchor stable");
                var beforePan = graph.ToScreen(PointF.Empty);
                Mouse(graph, "OnMouseDown", MouseButtons.Left, 10, 10); Mouse(graph, "OnMouseMove", MouseButtons.Left, 60, 80); Mouse(graph, "OnMouseUp", MouseButtons.Left, 60, 80);
                var afterPan = graph.ToScreen(PointF.Empty);
                Assert(afterPan.X - beforePan.X == 50 && afterPan.Y - beforePan.Y == 70, "canvas pans horizontally and vertically");
                views.LocateCurrent();
                RectangleF all = graph.LayoutData.Bounds;
                PointF reset = graph.ToScreen(new PointF(all.Left + all.Width / 2, all.Top + all.Height / 2));
                Assert(graph.Zoom == 1F && Math.Abs(reset.X - graph.Width / 2F) < 1 && Math.Abs(reset.Y - graph.Height / 2F) < 1, "main reset restores initial scale and centers entire graph after pan and zoom");
                graph.ZoomAt(.25F, anchor);
                typeof(TaskGraph).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(graph, new object[] { new KeyEventArgs(Keys.Home) });
                Assert(graph.Zoom == 1F && graph.ToScreen(new PointF(all.Left + all.Width / 2, all.Top + all.Height / 2)) == reset, "Home restores same initial graph viewport");
                // Force a vertical scrollbar and confirm it never pushes the status column offscreen.
                store.Change(s => { for (int i = 0; i < 80; i++) s.Tasks.Add(new Todo { Name = "row " + i }); });
                Descendants(main).OfType<Button>().First(x => x.Text.StartsWith("待办")).PerformClick(); views.SelectView(0); Application.DoEvents(); AssertColumns(grid);
                Descendants(main).OfType<Button>().First(x => x.Text.StartsWith("历史")).PerformClick(); views.SelectView(2);
                main.Hide();
                typeof(MainForm).GetMethod("ShowMain", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(main, new object[] { true });
                Assert(main.Visible && views.Mode == 0 && !views.ShowDone && grid.Rows.Count == store.Current.Tasks.Count(x => !x.Done), "expand entry resets history tree to active list");
            }
            using (var preview = new TaskPreview(store, b.Id))
            {
                preview.Show(); Application.DoEvents();
                var previewLayout = Descendants(preview).OfType<PreviewLayout>().Single();
                Assert(previewLayout.Controls[0].Height <= Ui.U(75) && previewLayout.Controls[1].Top <= Ui.U(95), "preview action row cannot consume large blank space");
                Assert(Descendants(preview).OfType<Button>().First(x => x.Text == "完成").BackColor == Theme.Chrome, "preview completion button uses neutral surface");
                var relationPane = Descendants(preview).OfType<TaskViews>().Single();
                Assert(relationPane.Height >= Ui.U(130) && relationPane.Bottom <= previewLayout.ClientSize.Height, "preview relationships remain visible below description");
                AssertPreviewTabs(relationPane);
                Capture(preview, "preview-list");
                Size originalSize = preview.Size;
                preview.Size = preview.MinimumSize; Application.DoEvents(); AssertPreviewTabs(relationPane);
                preview.Size = originalSize; Application.DoEvents(); AssertPreviewTabs(relationPane);
                using (var image = new Bitmap(relationPane.First.Width, relationPane.First.Height))
                {
                    relationPane.First.DrawToBitmap(image, relationPane.First.ClientRectangle);
                    Rectangle cell = relationPane.First.CellBounds(0, -1);
                    Color corner = image.GetPixel(cell.Left + 1, cell.Top + 1);
                    Color gap = image.GetPixel(cell.Left + 1, cell.Bottom - 1);
                    Assert(corner.R > 200 && corner.G > 200 && corner.B > 200 && gap.R > 200 && gap.G > 200 && gap.B > 200, "rounded header corners and bottom gap repaint without black remnants");
                }
                Assert(Descendants(preview).OfType<TextBox>().All(x => x.ReadOnly), "preview stays read-only");
                var views = Descendants(preview).OfType<TaskViews>().First();
                Assert(!views.Eye.Visible && Descendants(preview).OfType<Button>().Any(x => x.Text == "完成"), "preview list hides eye and uses completion button");
                Assert(views.First.Rows[1].Task.Important, "related list carries important marker");
                Assert(views.First.Rows.Count == 2 && views.First.Rows[1].Task.Id == a.Id, "prerequisites contain plus row and direct relation only");
                Assert(views.Second.Rows.Count == 3 && views.Second.Rows.Skip(1).All(r => r.Task.Id == c.Id || r.Task.Id == d.Id), "successors contain only the two direct relations");
                views.SelectView(2); views.Graph.ZoomAt(2F, new Point(20, 30)); views.LocateCurrent(); Capture(preview, "preview-tree");
                Assert(views.Graph.Zoom == 1F, "preview reset restores initial zoom");
                Assert(views.Eye.Visible, "preview tree shows eye");
                var rect = views.Graph.LayoutData.Nodes[b.Id].Bounds;
                PointF center = views.Graph.ToScreen(new PointF(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
                Assert(Math.Abs(center.X - views.Graph.Width / 2F) < 1 && Math.Abs(center.Y - views.Graph.Height / 2F) < 1, "preview tracking centers current task");
                views.SelectView(0); ClickCell(views.First, 1, 1);
                Assert(preview.CurrentId == b.Id && !views.Eye.Visible, "relation single click stays in current preview and list hides eye");
                DoubleClickCell(views.First, 1, 1);
                Assert(preview.CurrentId == a.Id && views.Second.Rows.Count == 3, "link navigation updates direct successors without indirect C");
                var markdown = Descendants(preview).OfType<MarkdownView>().Single();
                Assert(markdown.ReadOnly && markdown.Text.Contains("今日计划") && !markdown.Text.Contains("**"), "task preview renders Markdown read-only");
                markdown.Select(markdown.Text.IndexOf("关键", StringComparison.Ordinal), 2);
                Assert(markdown.SelectionFont != null && markdown.SelectionFont.Bold, "Markdown emphasis is formatted, not only stripped");
                Assert(Descendants(preview).OfType<Panel>().Any(x => x.Visible && x.AccessibleName == "重要任务"), "important preview title displays red icon");
                Assert(Descendants(preview).OfType<ReadOnlyText>().Any(x => x.Text.Contains(Rules.Countdown(a.Due, DateTime.Today))), "preview status includes countdown");
                var flagButton = Descendants(preview).OfType<Button>().First(x => x.AccessibleName == "取消重要标记");
                var titleMarker = Descendants(preview).OfType<Panel>().Single(x => x.AccessibleName == "重要任务");
                var taskTitle = Descendants(preview).OfType<ReadOnlyText>().Single(x => x.Text == a.Name);
                Assert(titleMarker.Width == (int)Math.Round(Ui.U(12) * taskTitle.Font.SizeInPoints / 9.5F) && titleMarker.Right == taskTitle.Left && Math.Abs(titleMarker.Top + titleMarker.Height / 2F - taskTitle.Top - taskTitle.Font.Height / 2F) <= 1, "title uses compact list-marker spacing with no extra margin and aligns to first-line center");
                Assert(flagButton.Image != null && flagButton.Image.Height <= Ui.U(13), "important action retains small exclamation icon");
                Capture(preview, "flag-preview");
                flagButton.PerformClick(); Application.DoEvents();
                Assert(!Rules.Get(store.Current, a.Id).Important && !Descendants(preview).OfType<Panel>().Any(x => x.Visible && x.AccessibleName == "重要任务"), "preview can remove important marker from the navigated task");
                Descendants(preview).OfType<Button>().First(x => x.AccessibleName == "设为重要标记").PerformClick(); Application.DoEvents();
                Assert(Rules.Get(store.Current, a.Id).Important, "preview can restore important marker");
                preview.GoBack(); Assert(preview.CurrentId == b.Id, "back returns to previous preview");
                preview.GoBack(); Assert(!preview.Visible, "back with no history closes preview");
            }
            store.Change(s => Rules.Complete(s, a.Id));
            using (var preview = new TaskPreview(store, a.Id))
            {
                preview.Show(); Application.DoEvents();
                var views = Descendants(preview).OfType<TaskViews>().First(); views.SelectView(2);
                Descendants(preview).OfType<Button>().First(x => x.Text == "恢复待办").PerformClick(); Application.DoEvents();
                Assert(!Rules.Get(store.Current, a.Id).Done, "completed preview text button restores task");
                Descendants(preview).OfType<Button>().First(x => x.Text == "完成").PerformClick(); Application.DoEvents();
                Assert(Rules.Get(store.Current, a.Id).Done, "preview completion text button saves task");
                Assert(views.ShowDone && views.Graph.LayoutData.Nodes.ContainsKey(a.Id), "completed preview initially includes its current node");
                views.SetShowDone(false); Assert(!views.Graph.LayoutData.Nodes.ContainsKey(a.Id), "closed eye hides every completed node");
                views.LocateCurrent(); Assert(views.ShowDone && views.Graph.LayoutData.Nodes.ContainsKey(a.Id), "track reopens eye to locate completed current task");
                bool closedBeforeDelete = false;
                preview.FormClosed += delegate { closedBeforeDelete = store.Current.Tasks.Any(t => t.Id == a.Id); };
                TaskActions.DeleteConfirmed(store, a.Id);
                Assert(closedBeforeDelete && !preview.Visible && !store.Current.Tasks.Any(t => t.Id == a.Id), "confirmed deletion closes matching preview before commit");
                Assert(!File.Exists(store.PathName + ".bak") && !Rules.Get(store.Current, b.Id).Prerequisites.Any(), "permanent deletion removes automatic backup and incident dependencies");
            }
            using (var preview = new TaskPreview(store, c.Id))
            {
                preview.Show(); Application.DoEvents(); preview.Navigate(b.Id); Application.DoEvents();
                TaskActions.DeleteConfirmed(store, c.Id); preview.GoBack();
                Assert(!preview.Visible, "back skips a deleted navigation target and closes");
            }
            using (var host = new AppWindow())
            using (var source = new TextBox { Multiline = true, MaxLength = 10000, Text = "example" })
            {
                var editor = new MarkdownEditor(source); host.Controls.Add(editor); host.Show(); Application.DoEvents();
                source.Select(0, 7); editor.Wrap("**", "**", "text");
                Assert(source.Text == "**example**" && source.SelectedText == "example", "Markdown toolbar wraps selected text and retains selection");
                int caret = source.SelectionStart; editor.SelectPreview(true); editor.SelectPreview(false);
                Assert(source.SelectionStart == caret && source.Text == "**example**", "preview toggle preserves source and caret");
                editor.SelectPreview(true);
                editor.Preview.ShowMarkdown("[site](https://example.com) `https://ignored.com` [unsafe](file:///C:/test.exe)");
                string opened = null; editor.Preview.OpenLink = url => opened = url;
                Assert(editor.Preview.ActivateLink(1) && opened == "https://example.com", "rendered label activates original web target");
                opened = null; Application.DoEvents();
                Point linkPoint = editor.Preview.GetPositionFromCharIndex(1); linkPoint.Offset(1, 3);
                Mouse(editor.Preview, "OnMouseDown", MouseButtons.Left, linkPoint.X, linkPoint.Y);
                Mouse(editor.Preview, "OnMouseUp", MouseButtons.Left, linkPoint.X, linkPoint.Y);
                Assert(opened == "https://example.com", "mouse click on rendered link opens its target");
                opened = null;
                Mouse(editor.Preview, "OnMouseDown", MouseButtons.Left, linkPoint.X - Ui.U(10), linkPoint.Y);
                Mouse(editor.Preview, "OnMouseUp", MouseButtons.Left, linkPoint.X, linkPoint.Y);
                Assert(opened == null, "drag selection does not open a link");
                Assert(!editor.Preview.ActivateLink(editor.Preview.Text.IndexOf("unsafe")), "non-web schemes cannot launch from Markdown");
            }
            VerifyHover(); VerifyStableScrolling(); VerifyRefinement(store); VerifyWindowRecovery(store); VerifyTrayMenu(store); VerifyInputIsolation(store); VerifyCompletionFeedback(temp);
            using (var toggle = new FloatingSwitch())
            {
                bool mode = false; toggle.Changed = value => { mode = value; toggle.IsFloating = value; };
                toggle.Choose(true); Assert(mode && toggle.IsFloating, "right capsule side selects floating mode");
                toggle.Choose(false); Assert(!mode && !toggle.IsFloating, "left capsule side selects window mode");
            }
            using (var date = new DateInput())
            {
                date.Value = new DateTime(2028, 2, 29); date.Checked = true;
                Assert(date.Checked && date.Value == new DateTime(2028, 2, 29), "custom date control preserves leap date and optional state");
                date.Checked = false; Assert(!date.Checked, "custom date can be cleared without changing retained date");
            }
            using (var markdown = new MarkdownView())
            {
                markdown.ShowMarkdown("> quote\n[link](https://example.com)");
                markdown.Select(markdown.Text.IndexOf("quote"), 5); Color quoteColor = markdown.SelectionColor;
                markdown.Select(markdown.Text.IndexOf("link"), 4);
                Assert(quoteColor == Theme.Quote && markdown.SelectionColor == Theme.Teal && quoteColor != markdown.SelectionColor, "quote and links use distinct taupe and blue families");
            }
            using (var cancelTimer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                cancelTimer.Tick += delegate { var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(x => x.Text == "确认测试"); if (dialog != null) { cancelTimer.Stop(); Assert(Descendants(dialog).OfType<TextBox>().All(x => x.ScrollBars == ScrollBars.None), "confirmation prompt has no native scrollbar"); dialog.Close(); } };
                cancelTimer.Start();
                Assert(ThemedDialog.Show("保留任务", "确认测试", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK,
                    "closing themed confirmation never confirms deletion");
            }
            using (var icon = new FloatingIcon(delegate { }, delegate(Point p) { }, new ContextMenuStrip()))
            { Assert(icon.ClientSize.Width == Ui.U(36) && icon.ClientSize.Height < Ui.U(36), "floating icon keeps 36 DIP width and trims transparent bottom padding"); }
            Console.WriteLine("All " + count + " UI assertions passed at scale " + Ui.Scale.ToString("0.00") + "."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { } }
    }
}
