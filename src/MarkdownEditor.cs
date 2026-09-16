using System;
using System.Drawing;
using System.Windows.Forms;

namespace TinyTodo
{
    internal sealed class MarkdownEditor : SurfacePanel
    {
        internal readonly TextBox Source;
        internal readonly MarkdownView Preview;
        private readonly Button write, preview;
        private readonly FlowLayoutPanel tools;
        private readonly ToolTip tips = new ToolTip();
        private bool previewing;
        private readonly TextViewport sourceHost, previewHost;
        internal MarkdownEditor(TextBox source)
        {
            Dock = DockStyle.Fill; Source = source; AccessibleName = "Markdown 编辑器";
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(Ui.Auto()); layout.RowStyles.Add(Ui.Auto()); layout.RowStyles.Add(Ui.Fill());
            var tabs = Ui.Bar();
            write = Ui.Button("编辑", delegate { SelectPreview(false); }); preview = Ui.Button("预览", delegate { SelectPreview(true); });
            tabs.Controls.Add(write); tabs.Controls.Add(preview);
            tools = Ui.Bar();
            Tool("H", "标题", delegate { Prefix("## "); });
            Tool("B", "粗体 · Ctrl+B", delegate { Wrap("**", "**", "粗体文字"); });
            Tool("I", "斜体 · Ctrl+I", delegate { Wrap("*", "*", "斜体文字"); });
            Tool("S", "删除线", delegate { Wrap("~~", "~~", "文字"); });
            Tool("链接", "插入链接 · Ctrl+K", InsertLink);
            Tool("引用", "引用", delegate { Prefix("> "); });
            Tool("代码", "代码块", delegate { Wrap("```\r\n", "\r\n```", "代码"); });
            Tool("列表", "无序列表", delegate { Prefix("- "); });
            Tool("待办", "任务列表", delegate { Prefix("- [ ] "); });
            Source.BorderStyle = BorderStyle.None; Source.AcceptsTab = true;
            Source.Font = Theme.Font(9.5F, FontStyle.Regular, "Markdown 编辑");
            Preview = new MarkdownView(); var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.U(4)) };
            sourceHost = new TextViewport(Source); previewHost = new TextViewport(Preview); body.Controls.Add(sourceHost); body.Controls.Add(previewHost);
            Source.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (!Ui.ExactModifiers(e, Keys.Control)) return;
                if (e.KeyCode == Keys.B) { Wrap("**", "**", "粗体文字"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.I) { Wrap("*", "*", "斜体文字"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.K) { InsertLink(); e.SuppressKeyPress = true; }
            };
            layout.Controls.Add(tabs, 0, 0); layout.Controls.Add(tools, 0, 1); layout.Controls.Add(body, 0, 2); Controls.Add(layout); SelectPreview(false);
        }
        private void Tool(string text, string tip, Action action)
        {
            var button = Ui.Button(text, delegate { action(); });
            button.Padding = new Padding(Ui.U(5), Ui.U(2), Ui.U(5), Ui.U(2)); button.AccessibleName = tip;
            if (text == "B") button.Font = Theme.Font(9.5F, FontStyle.Bold, text);
            if (text == "I") button.Font = Theme.Font(9.5F, FontStyle.Italic, text);
            tips.SetToolTip(button, tip); tools.Controls.Add(button);
        }
        internal void SelectPreview(bool value)
        {
            previewing = value; if (value) Preview.ShowMarkdown(Source.Text);
            previewHost.Visible = value; sourceHost.Visible = !value; Preview.Visible = value; Source.Visible = !value; tools.Visible = !value;
            write.BackColor = !value ? Theme.RoseSoft : Color.White; preview.BackColor = value ? Theme.RoseSoft : Color.White;
            if (!value) Source.Focus();
        }
        internal void Wrap(string left, string right, string placeholder)
        {
            if (previewing) SelectPreview(false);
            int start = Source.SelectionStart; string selected = Source.SelectedText;
            if (selected.Length == 0) selected = placeholder;
            string replacement = left + selected + right;
            if (Source.TextLength - Source.SelectionLength + replacement.Length > Source.MaxLength) return;
            Source.SelectedText = replacement; Source.Select(start + left.Length, selected.Length); Source.Focus();
        }
        internal void Prefix(string marker)
        {
            int start = Source.SelectionStart, end = start + Source.SelectionLength;
            int lineStart = start == 0 ? 0 : Source.Text.LastIndexOf('\n', start - 1) + 1;
            Source.Select(lineStart, end - lineStart);
            string selected = Source.SelectedText, replacement = marker + selected.Replace("\n", "\n" + marker);
            if (Source.TextLength - Source.SelectionLength + replacement.Length > Source.MaxLength) { Source.Select(start, end - start); return; }
            Source.SelectedText = replacement; Source.Select(lineStart, replacement.Length); Source.Focus();
        }
        internal void InsertLink()
        {
            int start = Source.SelectionStart; string label = Source.SelectedText; if (label.Length == 0) label = "链接文字";
            string target = "https://";
            try { if (Clipboard.ContainsText() && SimpleMarkdown.IsWebLink(Clipboard.GetText().Trim())) target = Clipboard.GetText().Trim(); } catch (System.Runtime.InteropServices.ExternalException) { }
            string replacement = "[" + label + "](" + target + ")";
            if (Source.TextLength - Source.SelectionLength + replacement.Length > Source.MaxLength) return;
            Source.SelectedText = replacement; Source.Select(start + label.Length + 3, target.Length); Source.Focus();
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }
}
