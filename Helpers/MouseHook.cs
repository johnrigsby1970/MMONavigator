using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MMONavigator.Helpers;

public static class MouseHook {
    private static NativeMethods.LowLevelMouseProc _proc = HookCallback;
    private static IntPtr _hookID = IntPtr.Zero;
    private static DateTime _lastClickTime = DateTime.MinValue;

    public static event Action<NativeMethods.Win32Point>? DoubleClickedOrCtrlClicked;

    public static void Start() {
        if (_hookID == IntPtr.Zero) {
            _hookID = SetHook(_proc);
        }
    }

    public static void Stop() {
        if (_hookID != IntPtr.Zero) {
            NativeMethods.UnhookWindowsHookEx(_hookID);
            _hookID = IntPtr.Zero;
        }
    }

    private static IntPtr SetHook(NativeMethods.LowLevelMouseProc proc) {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        return NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, 
            proc,
            NativeMethods.GetModuleHandle(curModule?.ModuleName), 
            0
        );
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_LBUTTONDOWN) {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            var now = DateTime.Now;

            // Check if VK_CONTROL (0x11) is down
            bool isCtrlPressed = (NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
            bool isDoubleClick = (now - _lastClickTime).TotalMilliseconds <= 350;

            if (isDoubleClick || isCtrlPressed) {
                DoubleClickedOrCtrlClicked?.Invoke(hookStruct.pt);
            }

            _lastClickTime = now;
        }

        return NativeMethods.CallNextHookEx(_hookID, nCode, wParam, lParam);
    }
}