using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QuickTranslate
{
    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "QuickTranslate";

        public static void EnsureEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key == null) throw new InvalidOperationException("无法设置开机启动");
                string expected = "\"" + Application.ExecutablePath + "\"";
                string current = Convert.ToString(key.GetValue(ValueName));
                if (!string.Equals(current, expected, StringComparison.Ordinal))
                {
                    key.SetValue(ValueName, expected, RegistryValueKind.String);
                }
            }
        }
    }
}
