using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TinyTodo;
internal static class CaptureDocs
{
    private static IEnumerable<Control> Children(Control root)
    { foreach (Control c in root.Controls) { yield return c; foreach (Control child in Children(c)) yield return child; } }
    private static void Save(Form form, string name)
    {
        Application.DoEvents();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(Path.Combine("docs/screenshots", name + ".png")); }
    }
    private static void Settle()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 650) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
    }
    private static void SaveCatToggle()
    {
        using (var canvas = new Bitmap(Ui.U(172), Ui.U(54)))
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Theme.Canvas);
            for (int i = 0; i < 2; i++)
                using (var toggle = new FloatingSwitch())
                {
                    toggle.IsFloating = i == 1; toggle.CreateControl();
                    using (var bitmap = new Bitmap(toggle.Width, toggle.Height))
                    { toggle.DrawToBitmap(bitmap, toggle.ClientRectangle); g.DrawImageUnscaled(bitmap, Ui.U(12 + i * 86), Ui.U(12)); }
                }
            canvas.Save("docs/screenshots/cat-toggle.png");
        }
    }
    [STAThread] private static void Main()
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        SaveCatToggle();
        string root = Path.GetFullPath("artifacts/docs-demo"); Directory.CreateDirectory(root);
        var store = new Store(Path.Combine(root, "tasks.json")); store.Load();
        var research = new Todo { Name = "整理想法", Due = "2026-10-14", Important = true, Description = "# 一个小计划\n\n- 记录灵感\n- 整理资料\n- 给每一步留一点时间" };
        var draft = new Todo { Name = "完成初稿", Due = "2026-10-16", Description = "## 今天的小目标\n\n先把想法写下来，再慢慢打磨。\n\n- [x] 整理大纲\n- [ ] 补充内容\n\n**一步一步来。**" }; draft.Prerequisites.Add(research.Id);
        var review = new Todo { Name = "检查与修改", Due = "2026-10-18" }; review.Prerequisites.Add(draft.Id);
        var publish = new Todo { Name = "发布作品", Due = "2026-10-21", Important = true }; publish.Prerequisites.Add(review.Id);
        var study = new Todo { Name = "每天读书", Due = DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd"), Repeat = new RepeatRule { Kind = RepeatKind.Daily }, Description = "每天留一点时间，读几页书，记下一个想法。" };
        store.Change(s => {
            s.Tasks.Clear(); s.FocusSessions.Clear(); s.Tasks.AddRange(new[] { research, draft, review, publish, study }); s.Window = new Settings { CatMode = CatVisibilityMode.Always };
            Rules.Complete(s, study.Id, DateTimeOffset.Now.AddDays(-2));
            Rules.Complete(s, s.Tasks.Single(t => !t.Done && t.SeriesId == study.Id).Id, DateTimeOffset.Now.AddDays(-1));
            for (int i = 0; i < 7; i++) s.FocusSessions.Add(new FocusSession { Id = "demo-" + i, TaskId = study.Id, SeriesId = study.Id,
                StartedAt = DateTimeOffset.Now.AddDays(-i).ToString("o"), Day = DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd"), Seconds = (20 + i * 5) * 60 });
        });
        using (var main = new MainForm(store, new DataLocations(root)))
        {
            main.Show(); main.Activate(); Save(main, "tasks");
            store.Focus.Start(draft.Id, 25); Settle(); Save(main, "countdown");
            store.Focus.Stop(); Settle();
            var views = Children(main).OfType<TaskViews>().Single();
            views.SelectView(2); Application.DoEvents();
            views.Graph.ZoomAt(.72F, new Point(views.Graph.Width / 2, views.Graph.Height / 2));
            views.Graph.CenterCurrent(); Save(main, "tree");
        }
        var currentStudy = store.Current.Tasks.Single(t => !t.Done && t.SeriesId == study.Id);
        using (var focus = new FocusForm(store, currentStudy.Id))
        { focus.Show(); focus.Activate(); Save(focus, "focus"); }
        using (var settings = new SettingsForm(store, new DataLocations(root)))
        {
            settings.AddBlacklist(new ProgramEntry { Name = "Visual Studio Code", Executable = "Code.exe" });
            settings.AddBlacklist(new ProgramEntry { Name = "Outlook", Executable = "OUTLOOK.exe" });
            settings.Show(); settings.Activate(); Application.DoEvents();
            Children(settings).OfType<TextBox>().Single(t => t.ReadOnly && t.Multiline).Text = @"C:\Users\You\AppData\Local\TinyTodo";
            Settle(); Save(settings, "settings");
            Children(settings).OfType<TextBox>().Single(t => t.AccessibleName == "GitHub 同步仓库").Text = "https://github.com/your-name/TinyTodo-storage";
            Children(settings).OfType<ScrollSurface>().Single().SetPosition(Int32.MaxValue);
            Settle(); Save(settings, "sync");
        }
    }
}
