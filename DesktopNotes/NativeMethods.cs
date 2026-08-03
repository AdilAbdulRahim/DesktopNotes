using System;
using System.Runtime.InteropServices;

namespace DesktopNotes;

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
