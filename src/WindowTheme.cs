using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ZapretDesktop
{
    internal static class WindowTheme
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        public static void Attach(Window window)
        {
            window.SourceInitialized += (s, e) => {
                var handle = new WindowInteropHelper(window).Handle;
                int enabled = 1, background = 0x000D0B0B, text = 0x00F4F2F0, border = 0x002C2626;
                // Unsupported attributes are ignored by older Windows versions.
                DwmSetWindowAttribute(handle, 20, ref enabled, 4);
                DwmSetWindowAttribute(handle, 35, ref background, 4);
                DwmSetWindowAttribute(handle, 36, ref text, 4);
                DwmSetWindowAttribute(handle, 34, ref border, 4);
            };
        }
    }
}
