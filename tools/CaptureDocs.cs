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
    [STAThread] private static void Main()
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string root = Path.GetFullPath("artifacts/docs-demo"); Directory.CreateDirectory(root);
        var store = new Store(Path.Combine(root, "tasks.json")); store.Load();
        var research = new Todo { Name = "整理想法", Due = "2026-09-18", Important = true, Description = "# 一个小计划\n\n- 记录灵感\n- 整理资料\n- 给每一步留一点时间" };
        var draft = new Todo { Name = "完成初稿", Due = "2026-09-20", Description = "## 今天的小目标\n\n先把想法写下来，再慢慢打磨。\n\n- [x] 整理大纲\n- [ ] 补充内容\n\n**一步一步来。**" }; draft.Prerequisites.Add(research.Id);
        var review = new Todo { Name = "检查与修改", Due = "2026-09-22" }; review.Prerequisites.Add(draft.Id);
        var publish = new Todo { Name = "发布作品", Due = "2026-09-25", Important = true }; publish.Prerequisites.Add(review.Id);
        store.Change(s => { s.Tasks.Clear(); s.Tasks.AddRange(new[] { research, draft, review, publish }); s.Window.Floating = false; });
        using (var main = new MainForm(store, new DataLocations(root)))
        {
            main.Show(); Save(main, "tasks");
            using (var preview = new TaskPreview(store, draft.Id))
            {
                preview.Show(main); Application.DoEvents();
                Children(preview).OfType<TaskViews>().Single().SelectView(2);
                Save(preview, "tree"); preview.Close();
            }
        }
    }
}
