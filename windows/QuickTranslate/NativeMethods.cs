using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Forms;

namespace QuickTranslate
{
    internal static class NativeMethods
    {
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;
        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const ushort VK_CONTROL = 0x11;
        public const ushort VK_C = 0x43;
        public const ushort VK_A = 0x41;
        public const ushort VK_V = 0x56;
        public const ushort VK_SPACE = 0x20;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        public static bool SendChord(ushort key)
        {
            INPUT[] inputs = new INPUT[4];
            inputs[0] = KeyboardInput(VK_CONTROL, 0);
            inputs[1] = KeyboardInput(key, 0);
            inputs[2] = KeyboardInput(key, KEYEVENTF_KEYUP);
            inputs[3] = KeyboardInput(VK_CONTROL, KEYEVENTF_KEYUP);
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) == inputs.Length;
        }

        public static bool IsEditableElementFocused()
        {
            try
            {
                IntPtr foreground = GetForegroundWindow();
                uint threadId = GetWindowThreadProcessId(foreground, IntPtr.Zero);
                GUITHREADINFO info = new GUITHREADINFO();
                info.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
                if (GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != IntPtr.Zero)
                {
                    StringBuilder className = new StringBuilder(256);
                    GetClassName(info.hwndFocus, className, className.Capacity);
                    string value = className.ToString().ToUpperInvariant();
                    if (value.Contains("EDIT") || value.Contains("RICHEDIT")) return true;
                }

                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused == null) return false;

                object password = focused.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true);
                if (password is bool && (bool)password) return false;

                object controlTypeValue = focused.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty, true);
                ControlType controlType = controlTypeValue as ControlType;
                if (controlType == ControlType.Edit) return true;

                if (controlType == ControlType.Document)
                {
                    object valuePattern = focused.GetCurrentPropertyValue(
                        AutomationElement.IsValuePatternAvailableProperty, true);
                    if (valuePattern is bool && (bool)valuePattern)
                    {
                        object readOnly = focused.GetCurrentPropertyValue(ValuePattern.IsReadOnlyProperty, true);
                        return !(readOnly is bool) || !(bool)readOnly;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool CanTranslateInFocusedApplication()
        {
            if (IsPasswordElementFocused()) return false;
            if (IsEditableElementFocused()) return true;

            try
            {
                IntPtr foreground = GetForegroundWindow();
                uint processId;
                GetWindowThreadProcessIdForProcess(foreground, out processId);
                if (processId == 0) return false;
                string name = Process.GetProcessById((int)processId).ProcessName;
                string[] compatibleApplications =
                {
                    "Weixin", "WeChat", "WeChatAppEx", "WXWork",
                    "Feishu", "Lark", "LarkShell", "ChatGPT"
                };
                foreach (string compatible in compatibleApplications)
                {
                    if (name.StartsWith(compatible, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private static bool IsPasswordElementFocused()
        {
            try
            {
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused == null) return false;
                object password = focused.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true);
                return password is bool && (bool)password;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        private static extern uint GetWindowThreadProcessIdForProcess(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maximumCount);

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        public static bool SendKey(ushort key)
        {
            INPUT[] inputs = new INPUT[2];
            inputs[0] = KeyboardInput(key, 0);
            inputs[1] = KeyboardInput(key, KEYEVENTF_KEYUP);
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) == inputs.Length;
        }

        private static INPUT KeyboardInput(ushort key, uint flags)
        {
            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.U.ki = new KEYBDINPUT
            {
                wVk = key,
                wScan = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };
            return input;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }
    }

}
