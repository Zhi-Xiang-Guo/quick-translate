using System.Drawing;
using System.Windows.Forms;

namespace QuickTranslate
{
    internal sealed class E2ETestForm : Form
    {
        public const string SourceText = "今天下午三点开会。";
        private readonly TextBox input;

        public E2ETestForm()
        {
            Text = "快捷翻译自动测试";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 130);
            Font = new Font("Microsoft YaHei UI", 11F);
            TopMost = true;

            input = new TextBox();
            input.Multiline = true;
            input.Text = SourceText;
            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(18);
            input.Font = new Font("Microsoft YaHei UI", 14F);

            Panel padding = new Panel();
            padding.Dock = DockStyle.Fill;
            padding.Padding = new Padding(18);
            padding.Controls.Add(input);
            Controls.Add(padding);

            Shown += delegate
            {
                input.Focus();
                input.SelectionStart = input.TextLength;
                input.SelectionLength = 0;
            };
        }

        public string CurrentText
        {
            get { return input.Text; }
        }

        public bool IsForeground
        {
            get { return NativeMethods.GetForegroundWindow() == Handle; }
        }

        public void ActivateInput()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            NativeMethods.SetForegroundWindow(Handle);
            input.Focus();
            input.SelectionStart = input.TextLength;
            input.SelectionLength = 0;
        }

        public void ActivateAndSelectAll()
        {
            ActivateInput();
            input.SelectAll();
        }
    }
}
