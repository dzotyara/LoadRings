using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LoadRings;

internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [StructLayout(LayoutKind.Sequential)]
    public sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowPos
    {
        public IntPtr Hwnd;
        public IntPtr HwndInsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
    }

    public const int GwlExStyle = -20;
    public const long WsExToolWindow = 0x80;
    public const int WmWindowPosChanging = 0x0046;
    public const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10;
    public static readonly IntPtr HwndBottom = new(1);

    // The app ships only as 64-bit (x64 / arm64), where the *Ptr variants exist.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}

/// Keeps the widget on the desktop layer: at the bottom of the z-order and out of Alt+Tab.
internal static class DesktopPinning
{
    public static void Attach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle,
                                       new IntPtr(exStyle.ToInt64() | NativeMethods.WsExToolWindow));
        HwndSource.FromHwnd(hwnd)?.AddHook(KeepAtBottom);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndBottom, 0, 0, 0, 0,
                                   NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    /// Whenever Windows is about to restack the widget (e.g. it was clicked), send it back to the bottom.
    private static IntPtr KeepAtBottom(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmWindowPosChanging)
        {
            var pos = Marshal.PtrToStructure<NativeMethods.WindowPos>(lParam);
            pos.HwndInsertAfter = NativeMethods.HwndBottom;
            pos.Flags &= ~NativeMethods.SwpNoZOrder;
            Marshal.StructureToPtr(pos, lParam, false);
        }
        return IntPtr.Zero;
    }
}
