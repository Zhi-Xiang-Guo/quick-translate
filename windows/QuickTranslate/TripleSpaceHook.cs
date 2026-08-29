using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickTranslate
{
    internal sealed class TripleSpaceHook : NativeWindow, IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_TRIGGER = 0x8001;
        private const uint VK_SPACE = 0x20;
        private const int MaximumIntervalMilliseconds = 700;

        private readonly HookProcedure procedure;
        private IntPtr hookHandle;
        private int spaceCount;
        private int lastSpaceTime;
        private bool spaceDown;
        private bool suppressSpaceUp;
        private IntPtr sequenceWindow;

        public event EventHandler Triggered;

        public TripleSpaceHook()
        {
            CreateHandle(new CreateParams { Caption = "QuickTranslate.KeyboardDispatcher" });
            procedure = HookCallback;
            hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, procedure, GetModuleHandle(null), 0);
            if (hookHandle == IntPtr.Zero)
            {
                DestroyHandle();
                throw new InvalidOperationException("无法启用三击空格监听");
            }
        }

        private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0) return CallNextHookEx(hookHandle, code, wParam, lParam);
            KeyboardData data = (KeyboardData)Marshal.PtrToStructure(lParam, typeof(KeyboardData));
            int message = wParam.ToInt32();
            bool keyDown = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
            bool keyUp = message == WM_KEYUP || message == WM_SYSKEYUP;

            if (data.VirtualKey == VK_SPACE && keyUp)
            {
                spaceDown = false;
                if (suppressSpaceUp)
                {
                    suppressSpaceUp = false;
                    return new IntPtr(1);
                }
            }
            else if (keyDown && data.VirtualKey == VK_SPACE)
            {
                if (spaceDown) return CallNextHookEx(hookHandle, code, wParam, lParam);
                spaceDown = true;

                int now = Environment.TickCount;
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                int elapsed = unchecked(now - lastSpaceTime);
                if (spaceCount == 0 || elapsed < 0 || elapsed > MaximumIntervalMilliseconds ||
                    foreground != sequenceWindow)
                {
                    spaceCount = 1;
                    sequenceWindow = foreground;
                }
                else
                {
                    spaceCount++;
                }
                lastSpaceTime = now;

                if (spaceCount >= 3)
                {
                    spaceCount = 0;
                    sequenceWindow = IntPtr.Zero;
                    suppressSpaceUp = true;
                    PostMessage(Handle, WM_TRIGGER, IntPtr.Zero, IntPtr.Zero);
                    return new IntPtr(1);
                }
            }
            else if (keyDown && data.VirtualKey != 0x10 && data.VirtualKey != 0x11 && data.VirtualKey != 0x12)
            {
                spaceCount = 0;
                sequenceWindow = IntPtr.Zero;
            }

            return CallNextHookEx(hookHandle, code, wParam, lParam);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WM_TRIGGER)
            {
                DiagnosticLog.Write("Triple-space dispatched to application");
                EventHandler handler = Triggered;
                if (handler != null) handler(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookHandle);
                hookHandle = IntPtr.Zero;
            }
            if (Handle != IntPtr.Zero) DestroyHandle();
        }

        private delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardData
        {
            public uint VirtualKey;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int hookId, HookProcedure callback, IntPtr module, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);
    }
}
