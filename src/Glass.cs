using System;
using System.Runtime.InteropServices;

namespace TokenMeter
{
    /// <summary>
    /// Windows 11 window chrome through DWM: rounded corners and the acrylic backdrop behind the
    /// panel. Everything here is best-effort - on Windows 10 or older builds the calls are skipped
    /// and the panel stays the plain opaque window it always was.
    /// </summary>
    public static class Glass
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Margins { public int Left, Right, Top, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct OsVersionInfo
        {
            public int Size, Major, Minor, Build, Platform;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string CsdVersion;
        }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);
        [DllImport("ntdll.dll")] private static extern int RtlGetVersion(ref OsVersionInfo v);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWCP_ROUND = 2;
        private const int DWMSBT_NONE = 1;
        private const int DWMSBT_TRANSIENTWINDOW = 3;   // acrylic

        private static int _build = -1;

        /// <summary>
        /// The real OS build. Environment.OSVersion lies (6.2) for an exe without a compatibility
        /// manifest, so ask ntdll directly.
        /// </summary>
        private static int Build
        {
            get
            {
                if (_build >= 0) return _build;
                try
                {
                    var v = new OsVersionInfo { Size = Marshal.SizeOf(typeof(OsVersionInfo)) };
                    _build = RtlGetVersion(ref v) == 0 ? v.Build : 0;
                }
                catch (Exception) { _build = 0; }
                return _build;
            }
        }

        /// <summary>Rounded corners arrived with Windows 11 (build 22000).</summary>
        public static bool HasRoundCorners { get { return Build >= 22000; } }

        /// <summary>The system backdrop attribute arrived with Windows 11 22H2 (build 22621).</summary>
        public static bool Supported { get { return Build >= 22621; } }

        public static void RoundCorners(IntPtr hwnd)
        {
            if (!HasRoundCorners) return;
            int v = DWMWCP_ROUND;
            try { DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, 4); } catch (Exception) { }
        }

        /// <summary>
        /// Turn the acrylic backdrop on or off. Returns whether glass is now active - the caller
        /// paints a translucent tint instead of an opaque fill only when this is true.
        /// </summary>
        public static bool Apply(IntPtr hwnd, bool on, bool dark)
        {
            if (!Supported) return false;
            try
            {
                int d = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, 4);
                // The backdrop only shows through the part of the window DWM treats as frame, so
                // extend the frame over the whole client area (-1) while glass is on.
                var m = on ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new Margins();
                DwmExtendFrameIntoClientArea(hwnd, ref m);
                int t = on ? DWMSBT_TRANSIENTWINDOW : DWMSBT_NONE;
                return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref t, 4) == 0 && on;
            }
            catch (Exception) { return false; }
        }
    }
}
