using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace QuickTranslate
{
    internal static class Program
    {
        private const string MutexName = "Local\\QuickTranslate.CCSwitch.4C72A8D1";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool isE2E = args.Length >= 2 && string.Equals(args[0], "--e2e", StringComparison.OrdinalIgnoreCase);
            bool isOfflineE2E = args.Length >= 2 && string.Equals(args[0], "--e2e-offline", StringComparison.OrdinalIgnoreCase);
            isE2E = isE2E || isOfflineE2E;
            bool showSettings = args.Length >= 1 && string.Equals(args[0], "--show-settings", StringComparison.OrdinalIgnoreCase);
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew && !isE2E)
                {
                    MessageBox.Show("快捷翻译已经在运行，请查看系统托盘。", "快捷翻译",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string resultPath = isE2E ? args[1] : null;
                Application.Run(new TranslationApplicationContext(isE2E, resultPath, showSettings, isOfflineE2E));
            }
        }
    }
}
