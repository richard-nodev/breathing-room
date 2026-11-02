using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace BreathingRoom
{
    internal static class Native
    {
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
        public const int WM_HOTKEY = 0x0312;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // Windows 11 rounds borderless windows only when asked.
        public static void RoundCorners(Window w)
        {
            try
            {
                int round = 2;
                DwmSetWindowAttribute(new WindowInteropHelper(w).Handle, 33, ref round, sizeof(int));
            }
            catch { }
        }

        // Overlays stay out of Alt-Tab and never take focus; click-through ones ignore the mouse entirely.
        public static void MakeOverlay(Window w, bool clickThrough)
        {
            IntPtr hwnd = new WindowInteropHelper(w).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            if (clickThrough) ex |= WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }

        // Borderless games also sit on top; nudging our z-order now and then keeps the overlay visible.
        public static void KeepOnTop(Window w)
        {
            IntPtr hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public static int IdleMilliseconds()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref info)) return 0;
            return (int)((uint)Environment.TickCount - info.dwTime);
        }

        // Exclusive-fullscreen games draw over every window, so the app has to speak instead.
        public static bool ExclusiveFullscreen()
        {
            int state;
            try { return SHQueryUserNotificationState(out state) == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN; }
            catch { return false; }
        }

        public const string Desktop = "Desktop";

        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

        // The name an app gives itself ("Google Chrome", "Visual Studio Code"), read from its exe.
        // Works for most apps, elevated ones included; falls back to the program's file name.
        static string FriendlyName(uint pid, string processName)
        {
            try
            {
                IntPtr h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
                if (h != IntPtr.Zero)
                {
                    try
                    {
                        var path = new StringBuilder(1024);
                        int size = path.Capacity;
                        if (QueryFullProcessImageName(h, 0, path, ref size))
                        {
                            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path.ToString());
                            foreach (string candidate in new[] { info.FileDescription, info.ProductName })
                            {
                                string n = candidate == null ? "" : candidate.Trim();
                                if (n.Length > 0 && n.Length <= 40 && n != "Electron" && !n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                    return n;
                            }
                        }
                    }
                    finally { CloseHandle(h); }
                }
            }
            catch { }
            return AppLabel(processName);
        }

        // "claude" -> "Claude": program names saved before friendly names existed.
        public static string AppLabel(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return name == name.ToLowerInvariant() ? char.ToUpperInvariant(name[0]) + name.Substring(1) : name;
        }

        public static void Foreground(out string appName, out bool fullscreen)
        {
            appName = null;
            fullscreen = false;
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return;
                var cls = new StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") { appName = Desktop; return; }

                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == (uint)Process.GetCurrentProcess().Id) return;
                string processName;
                using (var p = Process.GetProcessById((int)pid)) processName = p.ProcessName;
                appName = FriendlyName(pid, processName);

                RECT r;
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (GetWindowRect(hwnd, out r) && GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref mi))
                    fullscreen = r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top &&
                                 r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
            }
            catch { }
        }

        // Cursor position in WPF units (DIPs) for the given window's screen.
        public static Point CursorDip(Visual relativeTo)
        {
            POINT p;
            GetCursorPos(out p);
            var source = PresentationSource.FromVisual(relativeTo);
            if (source == null || source.CompositionTarget == null) return new Point(p.X, p.Y);
            return source.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));
        }
    }
}
