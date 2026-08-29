using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuickTranslate
{
    internal sealed class SettingsForm : Form
    {
        private readonly TranslationClient client;
        private readonly Label providerValue;
        private readonly Label modelValue;
        private readonly TextBox endpointValue;
        private readonly Label configStatus;
        private readonly Label operationStatus;
        private readonly Button testButton;
        private bool allowClose;

        public SettingsForm(TranslationClient client)
        {
            this.client = client;

            Text = "快捷翻译设置";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(610, 430);
            MinimumSize = new Size(590, 445);
            BackColor = Color.FromArgb(247, 248, 250);
            Font = new Font("Microsoft YaHei UI", 9.5F);
            Icon = SystemIcons.Information;

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 82;
            header.BackColor = Color.White;

            Label title = new Label();
            title.Text = "快捷翻译";
            title.Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(27, 31, 36);
            title.AutoSize = true;
            title.Location = new Point(24, 15);

            Label subtitle = new Label();
            subtitle.Text = "CC Switch / Codex 接口";
            subtitle.Font = new Font("Microsoft YaHei UI", 9F);
            subtitle.ForeColor = Color.FromArgb(92, 99, 112);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(27, 53);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            Controls.Add(header);

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(24, 18, 24, 16);
            body.ColumnCount = 2;
            body.RowCount = 8;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

            body.Controls.Add(MakeFieldLabel("供应商"), 0, 0);
            providerValue = MakeValueLabel();
            body.Controls.Add(providerValue, 1, 0);

            body.Controls.Add(MakeFieldLabel("模型"), 0, 1);
            modelValue = MakeValueLabel();
            body.Controls.Add(modelValue, 1, 1);

            body.Controls.Add(MakeFieldLabel("接口"), 0, 2);
            endpointValue = new TextBox();
            endpointValue.ReadOnly = true;
            endpointValue.Dock = DockStyle.Fill;
            endpointValue.BackColor = Color.White;
            endpointValue.BorderStyle = BorderStyle.FixedSingle;
            endpointValue.Margin = new Padding(0, 6, 0, 8);
            body.Controls.Add(endpointValue, 1, 2);

            body.Controls.Add(MakeFieldLabel("配置状态"), 0, 3);
            configStatus = MakeValueLabel();
            body.Controls.Add(configStatus, 1, 3);

            body.Controls.Add(MakeFieldLabel("触发方式"), 0, 4);
            Label triggerValue = MakeValueLabel();
            triggerValue.Text = "快速连续按 3 次空格";
            body.Controls.Add(triggerValue, 1, 4);

            Label privacy = new Label();
            privacy.Text = "API Key 由 CC Switch 管理，本程序不保存密钥。";
            privacy.ForeColor = Color.FromArgb(92, 99, 112);
            privacy.Dock = DockStyle.Fill;
            privacy.TextAlign = ContentAlignment.MiddleLeft;
            privacy.AutoEllipsis = true;
            body.SetColumnSpan(privacy, 2);
            body.Controls.Add(privacy, 0, 5);

            operationStatus = new Label();
            operationStatus.Text = "就绪";
            operationStatus.ForeColor = Color.FromArgb(64, 73, 87);
            operationStatus.Dock = DockStyle.Fill;
            operationStatus.TextAlign = ContentAlignment.TopLeft;
            operationStatus.AutoEllipsis = true;
            operationStatus.Padding = new Padding(0, 10, 0, 0);
            body.SetColumnSpan(operationStatus, 2);
            body.Controls.Add(operationStatus, 0, 6);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.WrapContents = false;
            actions.Margin = new Padding(0);
            Button hideButton = MakeButton("隐藏到托盘", false);
            hideButton.Click += delegate { Hide(); };
            testButton = MakeButton("测试接口", false);
            testButton.Click += async delegate { await TestApiAsync(); };
            actions.Controls.Add(hideButton);
            actions.Controls.Add(testButton);
            body.SetColumnSpan(actions, 2);
            body.Controls.Add(actions, 0, 7);
            Controls.Add(body);
            header.BringToFront();

            FormClosing += OnFormClosing;
        }

        public void RefreshConfiguration()
        {
            try
            {
                CcSwitchConfig config = CcSwitchConfig.LoadCurrent();
                providerValue.Text = config.ProviderName;
                modelValue.Text = config.Model;
                endpointValue.Text = config.Endpoint;
                configStatus.Text = "已连接";
                configStatus.ForeColor = Color.FromArgb(22, 128, 79);
                operationStatus.Text = "就绪";
                operationStatus.ForeColor = Color.FromArgb(64, 73, 87);
            }
            catch (Exception error)
            {
                providerValue.Text = "-";
                modelValue.Text = "-";
                endpointValue.Text = string.Empty;
                configStatus.Text = "不可用";
                configStatus.ForeColor = Color.FromArgb(190, 52, 52);
                operationStatus.Text = error.Message;
                operationStatus.ForeColor = Color.FromArgb(190, 52, 52);
            }
        }

        private async Task TestApiAsync()
        {
            testButton.Enabled = false;
            operationStatus.Text = "正在测试接口...";
            operationStatus.ForeColor = Color.FromArgb(64, 73, 87);
            try
            {
                string result = await client.TranslateAsync("你好，世界！");
                operationStatus.Text = "接口正常 · " + result;
                operationStatus.ForeColor = Color.FromArgb(22, 128, 79);
                RefreshConfiguration();
            }
            catch (Exception error)
            {
                operationStatus.Text = error.Message;
                operationStatus.ForeColor = Color.FromArgb(190, 52, 52);
            }
            finally
            {
                testButton.Enabled = true;
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs eventArgs)
        {
            if (!allowClose && eventArgs.CloseReason == CloseReason.UserClosing)
            {
                eventArgs.Cancel = true;
                Hide();
            }
        }

        protected override void Dispose(bool disposing)
        {
            allowClose = true;
            base.Dispose(disposing);
        }

        private static Label MakeFieldLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = Color.FromArgb(92, 99, 112);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            return label;
        }

        private static Label MakeValueLabel()
        {
            Label label = new Label();
            label.ForeColor = Color.FromArgb(27, 31, 36);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            return label;
        }

        private static Button MakeButton(string text, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = false;
            button.Size = new Size(102, 34);
            button.Margin = new Padding(8, 3, 0, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            if (primary)
            {
                button.BackColor = Color.FromArgb(35, 99, 183);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(35, 99, 183);
            }
            else
            {
                button.BackColor = Color.White;
                button.ForeColor = Color.FromArgb(38, 44, 55);
                button.FlatAppearance.BorderColor = Color.FromArgb(199, 204, 213);
            }
            return button;
        }
    }
}
