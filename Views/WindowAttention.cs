using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DevPilot.Views;

/// <summary>Flashes a window's taskbar button until the user switches to it.</summary>
public static class WindowAttention
{
    private const uint FlashAll = 0x3;           // caption and taskbar button
    private const uint FlashUntilForeground = 0xC;

    public static void Flash(Window window)
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = new WindowInteropHelper(window).Handle,
            Flags = FlashAll | FlashUntilForeground,
            Count = uint.MaxValue,
            Timeout = 0
        };
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}
