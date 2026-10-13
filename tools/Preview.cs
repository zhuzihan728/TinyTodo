using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using TinyTodo;

internal static class Preview
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        bool first;
        using (var mutex = new Mutex(true, @"Local\TinyTodo.Preview.2026", out first))
        {
            if (!first) return;
            try
            {
                string folder = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../artifacts/productivity/trial"));
                var locations = new DataLocations(folder); locations.Load();
                if (!File.Exists(locations.TaskPath))
                {
                    var original = DataLocations.Default();
                    if (File.Exists(Path.Combine(original.MetadataDirectory, "data-locations.json")))
                    {
                        original.Load();
                        if (File.Exists(original.TaskPath)) File.Copy(original.TaskPath, locations.TaskPath);
                    }
                }
                var store = new Store(locations.TaskPath); store.Load();
                using (var form = new MainForm(store, locations))
                {
                    Action label = delegate { if (!form.Text.EndsWith(" · 试用副本")) form.Text += " · 试用副本"; };
                    form.Shown += delegate { label(); };
                    store.Changed += delegate { if (!form.IsDisposed && form.IsHandleCreated) form.BeginInvoke(label); };
                    Application.Run(form);
                }
            }
            finally { mutex.ReleaseMutex(); }
        }
    }
}
