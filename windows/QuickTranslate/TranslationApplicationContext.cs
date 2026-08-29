using System;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace QuickTranslate
{
    internal sealed class TranslationApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon trayIcon;
        private TripleSpaceHook triggerHook;
        private readonly TranslationClient client;
        private readonly bool e2eMode;
        private readonly string e2eResultPath;
        private readonly bool offlineE2E;
        private SettingsForm settingsForm;
        private E2ETestForm e2eForm;
        private Timer e2eTimeoutTimer;
        private bool busy;
        private bool exiting;
        private ClipboardSnapshot e2eOriginalClipboard;
        private const string E2EClipboardSentinel = "QuickTranslate clipboard restore test";

        public TranslationApplicationContext(bool e2eMode, string e2eResultPath, bool showSettings, bool offlineE2E)
        {
            this.e2eMode = e2eMode;
            this.e2eResultPath = e2eResultPath;
            this.offlineE2E = offlineE2E;
            client = new TranslationClient();
            DiagnosticLog.Write("Application started");

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("打开设置", null, delegate { ShowSettings(); });
            menu.Items.Add("测试接口", null, async delegate { await TestApiFromTrayAsync(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitApplication(); });

            trayIcon = new NotifyIcon();
            trayIcon.Icon = SystemIcons.Information;
            trayIcon.Text = "快捷翻译";
            trayIcon.ContextMenuStrip = menu;
            trayIcon.Visible = !e2eMode;
            trayIcon.DoubleClick += delegate { ShowSettings(); };

            try
            {
                triggerHook = new TripleSpaceHook();
                triggerHook.Triggered += TripleSpaceTriggered;
            }
            catch (Exception error)
            {
                if (e2eMode) FinishE2E(false, error.Message, null);
                else ShowBalloon("启动失败", error.Message, ToolTipIcon.Error);
                return;
            }

            if (e2eMode)
            {
                e2eOriginalClipboard = ClipboardSnapshot.Capture();
                ClipboardSnapshot.SetText(E2EClipboardSentinel);
                StartE2E();
            }
            else
            {
                try
                {
                    StartupManager.EnsureEnabled();
                }
                catch (Exception error)
                {
                    ShowBalloon("开机启动设置失败", error.Message, ToolTipIcon.Warning);
                }
                ShowBalloon("快捷翻译已启动", "在输入框内快速连按 3 次空格", ToolTipIcon.Info);
                if (showSettings) ShowSettings();
            }
        }

        private async void TripleSpaceTriggered(object sender, EventArgs eventArgs)
        {
            await TranslateCurrentInputAsync();
        }

        private async Task TranslateCurrentInputAsync()
        {
            if (busy)
            {
                DiagnosticLog.Write("Trigger ignored because a translation is already running");
                return;
            }
            busy = true;
            ClipboardSnapshot previousClipboard = null;
            string source = null;
            try
            {
                IntPtr originalWindow = NativeMethods.GetForegroundWindow();
                if (e2eMode)
                {
                    if (!await EnsureE2EForegroundAsync())
                    {
                        throw new InvalidOperationException("自动测试窗口无法获得前台焦点");
                    }
                    originalWindow = NativeMethods.GetForegroundWindow();
                }
                DiagnosticLog.Write("Triple-space trigger; " + NativeMethods.DescribeFocusedApplication());
                bool editableInputFocused = NativeMethods.CanTranslateInFocusedApplication();
                if (!editableInputFocused)
                {
                    throw new InvalidOperationException("请先把光标放入可编辑输入框");
                }
                previousClipboard = ClipboardSnapshot.Capture();
                ClipboardSnapshot.Clear();
                if (!NativeMethods.SendChord(NativeMethods.VK_A))
                {
                    throw new InvalidOperationException("无法选择当前输入框内容");
                }
                await Task.Delay(150);
                if (!NativeMethods.SendChord(NativeMethods.VK_C))
                {
                    throw new InvalidOperationException("无法复制当前输入框内容");
                }
                for (int attempt = 0; attempt < 50; attempt++)
                {
                    await Task.Delay(40);
                    source = ClipboardSnapshot.GetText();
                    if (!string.IsNullOrWhiteSpace(source)) break;
                }
                if (string.IsNullOrWhiteSpace(source))
                {
                    throw new InvalidOperationException("当前输入框中没有可翻译的文字");
                }
                source = source.TrimEnd();
                if (!Regex.IsMatch(source, @"[\u3400-\u9fff]"))
                {
                    throw new InvalidOperationException("当前输入框中没有中文");
                }
                DiagnosticLog.Write("Input captured; characters=" + source.Length);

                SetTrayStatus("快捷翻译 - 正在翻译");
                string translated;
                if (offlineE2E)
                {
                    await Task.Delay(120);
                    translated = "The meeting is at 3 p.m. today.";
                }
                else
                {
                    translated = await client.TranslateAsync(source);
                }

                if (e2eMode && e2eForm != null)
                {
                    e2eForm.ActivateAndSelectAll();
                    await Task.Delay(100);
                    originalWindow = NativeMethods.GetForegroundWindow();
                }
                if (NativeMethods.GetForegroundWindow() != originalWindow)
                {
                    ClipboardSnapshot.SetText(translated);
                    previousClipboard = null;
                    ShowBalloon("译文已复制", "检测到焦点已切换，没有自动粘贴。", ToolTipIcon.Warning);
                    FinishE2E(false, "翻译期间焦点发生变化", translated);
                    return;
                }

                ClipboardSnapshot.SetText(translated);
                if (!NativeMethods.SendChord(NativeMethods.VK_V))
                {
                    throw new InvalidOperationException("无法发送粘贴快捷键");
                }
                await Task.Delay(450);
                previousClipboard.Restore();
                previousClipboard = null;
                SetTrayStatus("快捷翻译");
                DiagnosticLog.Write("Translation pasted successfully; characters=" + translated.Length);

                FinishE2E(true, null, translated);
            }
            catch (Exception error)
            {
                string safeError = error.Message;
                if (!string.IsNullOrEmpty(source)) safeError = safeError.Replace(source, "[redacted]");
                DiagnosticLog.Write("Translation failed; " + error.GetType().Name + ": " + safeError);
                if (previousClipboard != null)
                {
                    try { previousClipboard.Restore(); }
                    catch { }
                }
                SetTrayStatus("快捷翻译");
                if (!e2eMode)
                {
                    ShowBalloon("翻译失败", error.Message, ToolTipIcon.Error);
                }
                FinishE2E(false, error.Message, null);
            }
            finally
            {
                busy = false;
            }
        }

        private void StartE2E()
        {
            e2eForm = new E2ETestForm();
            e2eTimeoutTimer = new Timer();
            e2eTimeoutTimer.Interval = 110000;
            e2eTimeoutTimer.Tick += delegate
            {
                e2eTimeoutTimer.Stop();
                FinishE2E(false, "三击空格自动测试超时", null);
            };
            e2eTimeoutTimer.Start();
            e2eForm.Shown += async delegate
            {
                await Task.Delay(700);
                for (int press = 0; press < 3; press++)
                {
                    if (!await EnsureE2EForegroundAsync())
                    {
                        FinishE2E(false, "自动测试窗口无法获得前台焦点", null);
                        return;
                    }
                    bool sent = NativeMethods.SendKey(NativeMethods.VK_SPACE);
                    DiagnosticLog.Write("E2E space " + (press + 1) + "; sent=" + sent + "; " +
                        NativeMethods.DescribeFocusedApplication());
                    await Task.Delay(120);
                }
            };
            e2eForm.Show();
        }

        private async Task<bool> EnsureE2EForegroundAsync()
        {
            if (e2eForm == null) return false;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                e2eForm.ActivateInput();
                await Task.Delay(100);
                if (e2eForm.IsForeground) return true;
            }
            return false;
        }

        private void FinishE2E(bool success, string error, string translated)
        {
            if (!e2eMode || exiting) return;
            exiting = true;
            if (e2eTimeoutTimer != null) e2eTimeoutTimer.Stop();
            try
            {
                string textbox = e2eForm == null ? null : e2eForm.CurrentText;
                bool clipboardRestored = string.Equals(ClipboardSnapshot.GetText(), E2EClipboardSentinel,
                    StringComparison.Ordinal);
                bool replaced = success && !string.IsNullOrWhiteSpace(textbox) &&
                    !Regex.IsMatch(textbox, @"[\u3400-\u9fff]") && textbox == translated;
                DictionaryResult result = new DictionaryResult
                {
                    Success = success && replaced && clipboardRestored,
                    Error = success && !replaced ? "译文未正确替换测试输入框" :
                        (success && !clipboardRestored ? "原剪贴板内容未恢复" : error),
                    Source = E2ETestForm.SourceText,
                    Translation = translated,
                    Textbox = textbox,
                    Shortcut = "Space x3",
                    ClipboardRestored = clipboardRestored
                };
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Directory.CreateDirectory(Path.GetDirectoryName(e2eResultPath));
                File.WriteAllText(e2eResultPath, serializer.Serialize(result));
            }
            catch
            {
            }
            finally
            {
                try
                {
                    if (e2eOriginalClipboard != null) e2eOriginalClipboard.Restore();
                }
                catch { }
            }
            ExitApplication();
        }

        private async Task TestApiFromTrayAsync()
        {
            if (busy) return;
            busy = true;
            try
            {
                string result = await client.TranslateAsync("你好，世界！");
                ShowBalloon("接口正常", "测试译文：" + result, ToolTipIcon.Info);
            }
            catch (Exception error)
            {
                ShowBalloon("接口测试失败", error.Message, ToolTipIcon.Error);
            }
            finally
            {
                busy = false;
            }
        }

        private void ShowSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed)
            {
                settingsForm = new SettingsForm(client);
            }
            settingsForm.Show();
            settingsForm.WindowState = FormWindowState.Normal;
            settingsForm.Activate();
            settingsForm.RefreshConfiguration();
        }

        private void SetTrayStatus(string value)
        {
            trayIcon.Text = value.Length > 63 ? value.Substring(0, 63) : value;
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            if (e2eMode) return;
            trayIcon.BalloonTipTitle = title;
            trayIcon.BalloonTipText = text.Length > 240 ? text.Substring(0, 240) + "..." : text;
            trayIcon.BalloonTipIcon = icon;
            trayIcon.ShowBalloonTip(3500);
        }

        private void ExitApplication()
        {
            DiagnosticLog.Write("Application stopped");
            exiting = true;
            trayIcon.Visible = false;
            if (triggerHook != null) triggerHook.Dispose();
            trayIcon.Dispose();
            if (settingsForm != null) settingsForm.Dispose();
            if (e2eForm != null) e2eForm.Dispose();
            if (e2eTimeoutTimer != null) e2eTimeoutTimer.Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !exiting) ExitApplication();
            base.Dispose(disposing);
        }

        private sealed class DictionaryResult
        {
            public bool Success { get; set; }
            public string Error { get; set; }
            public string Source { get; set; }
            public string Translation { get; set; }
            public string Textbox { get; set; }
            public string Shortcut { get; set; }
            public bool ClipboardRestored { get; set; }
        }
    }
}
