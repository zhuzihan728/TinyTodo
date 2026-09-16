using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TinyTodo
{
    // Modeless windows share a Store, not a native owner: minimizing one does not hide others.
    internal static class TaskWindows
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);
        internal static void ShowPassive(Form form)
        {
            // WM_SHOWWINDOW synchronizes Visible/VisibleChanged without Form.Show's
            // extra focus call for topmost forms. SW_SHOWNA preserves position and focus.
            ShowWindow(form.Handle, 8);
        }
        private sealed class Entry { internal Store Store; internal Form Form; }
        private static readonly List<Entry> windows = new List<Entry>();
        private static readonly HashSet<AppWindow> fullscreenHidden = new HashSet<AppWindow>();
        internal static void Watch(Store store, Form form, Action refresh)
        {
            bool queued = false;
            EventHandler changed = delegate
            {
                if (queued || form.IsDisposed || form.Disposing || !form.IsHandleCreated) return;
                queued = true;
                form.BeginInvoke(new Action(delegate { queued = false; if (!form.IsDisposed && !form.Disposing) refresh(); }));
            };
            store.Changed += changed;
            form.Disposed += delegate { store.Changed -= changed; };
        }
        internal static T Find<T>(Store store, Func<T, bool> match) where T : Form
        { return windows.Where(e => e.Store == store && !e.Form.IsDisposed).Select(e => e.Form).OfType<T>().FirstOrDefault(match); }
        internal static void Present(Form form, Form origin)
        {
            ForgetAutomaticHide(form);
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            if (!form.Visible)
            {
                if (!form.IsHandleCreated && origin != null)
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(origin.Left + Ui.U(32), origin.Top + Ui.U(32));
                }
                form.Show(); Ui.Fit(form);
            }
            form.BringToFront(); form.Activate();
        }
        internal static void Show(Store store, Form form, Form origin)
        {
            windows.Add(new Entry { Store = store, Form = form });
            form.Disposed += delegate { windows.RemoveAll(e => e.Form == form); };
            form.FormClosed += delegate { form.Dispose(); };
            Present(form, origin);
        }
        internal static TaskEditor Edit(Store store, Form origin, string id, string anchor = null, bool before = false)
        {
            var existing = Find<TaskEditor>(store, e => e.TaskId == id && e.AnchorId == anchor && e.Before == before);
            if (existing != null) { Present(existing, origin); existing.ConflictFeedback(); return existing; }
            if (anchor != null)
            {
                var busy = Find<TaskEditor>(store, e => e.TaskId == anchor);
                if (busy != null) { Feedback(origin, busy); return null; }
            }
            var editor = new TaskEditor(store, id == null ? null : Rules.Get(store.Current, id), anchor, before);
            Show(store, editor, origin); return editor;
        }
        internal static TaskPreview Preview(Store store, Form origin, string id)
        {
            var existing = Find<TaskPreview>(store, p => p.CurrentId == id);
            if (existing != null) { Present(existing, origin); return existing; }
            var preview = new TaskPreview(store, id); Show(store, preview, origin); return preview;
        }
        internal static bool CanChange(Store store, Action<State> change, Form origin, TaskEditor except = null)
        {
            var editors = windows.Where(e => e.Store == store).Select(e => e.Form).OfType<TaskEditor>().Where(e => e != except && !e.IsDisposed).ToArray();
            if (editors.Length == 0) return true;
            var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 128 };
            State next = serializer.Deserialize<State>(serializer.Serialize(store.Current));
            change(next);
            foreach (var editor in editors)
                if (editor.Conflicts(store.Current, next)) { Feedback(origin, editor); return false; }
            return true;
        }
        private static void Feedback(Form origin, TaskEditor editor)
        {
            var window = origin as AppWindow; if (window != null) window.ConflictFeedback();
            // Restore only in response to the conflicting click, never during background polling.
            Present(editor, origin); editor.ConflictFeedback();
        }
        internal static void ForgetAutomaticHide(Form form)
        { var window = form as AppWindow; if (window != null) fullscreenHidden.Remove(window); }
        internal static void UpdateLayers() { UpdateFullscreen(DesktopActivity.ShouldYieldOn); }
        internal static void UpdateFullscreen(Func<Rectangle, bool> shouldHide)
        {
            fullscreenHidden.RemoveWhere(f => f.IsDisposed);
            foreach (var form in Application.OpenForms.Cast<Form>().OfType<AppWindow>().ToArray())
            {
                if (form.IsDisposed || form.WindowState == FormWindowState.Minimized) continue;
                bool hide = shouldHide(Screen.FromControl(form).Bounds);
                if (hide && form.Visible)
                {
                    fullscreenHidden.Add(form);
                    form.Hide();
                }
                else if (!hide && fullscreenHidden.Remove(form))
                {
                    // Restore visibility and location without activation or moving other windows.
                    ShowPassive(form);
                }
            }
        }
        internal static void CloseAll(Store store)
        { foreach (var entry in windows.Where(e => e.Store == store).ToArray()) entry.Form.Dispose(); windows.RemoveAll(e => e.Store == store); }
    }
}
