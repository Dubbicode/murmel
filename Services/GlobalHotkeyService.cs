using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Murmel.Services;

/// <summary>
/// Curated set of modifier-combo hotkeys the user can pick from in Settings.
/// We deliberately don't allow arbitrary single keys anymore - combos are far less
/// likely to collide with shortcuts other apps already use.
/// </summary>
public enum HotkeyPreset
{
    CtrlShift,
    CtrlWin,
    CtrlAlt,
    WinShift,
    WinAlt,
    AltGr
}

public static class HotkeyPresetInfo
{
    public static readonly HotkeyPreset[] All =
    {
        HotkeyPreset.CtrlShift,
        HotkeyPreset.CtrlWin,
        HotkeyPreset.CtrlAlt,
        HotkeyPreset.WinShift,
        HotkeyPreset.WinAlt,
        HotkeyPreset.AltGr
    };

    public static string GetDisplayName(HotkeyPreset preset) => preset switch
    {
        HotkeyPreset.CtrlShift => "Strg + Shift",
        HotkeyPreset.CtrlWin => "Strg + Windows",
        HotkeyPreset.CtrlAlt => "Strg + Alt",
        HotkeyPreset.WinShift => "Windows + Shift",
        HotkeyPreset.WinAlt => "Windows + Alt",
        HotkeyPreset.AltGr => "Alt Gr",
        _ => preset.ToString()
    };
}

/// <summary>
/// Push-to-talk global hotkey: hold the configured modifier combo down anywhere in
/// Windows (even while another app has focus) to start recording, release either key
/// to stop.
///
/// Implemented as a low-level keyboard hook (WH_KEYBOARD_LL) rather than the classic
/// RegisterHotKey Win32 API, because RegisterHotKey needs our own window's message loop
/// (WndProc), which Avalonia doesn't expose easily. A low-level hook works system-wide,
/// independent of which window is focused, and only needs a normal Win32 message pump
/// running on the thread that installed it (the UI thread already has one on Windows).
///
/// Since a preset is a combination of two modifier keys (e.g. Strg + Shift), we track
/// every currently-pressed key in a set and evaluate whether the combo is "active"
/// (both sides down) after every key event, rather than matching a single VK code.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    // Left/right variants both count, so either physical key satisfies that side of the combo.
    private static readonly int[] CtrlKeys = { 0x11, 0xA2, 0xA3 };   // VK_CONTROL, VK_LCONTROL, VK_RCONTROL
    private static readonly int[] ShiftKeys = { 0x10, 0xA0, 0xA1 };  // VK_SHIFT, VK_LSHIFT, VK_RSHIFT
    private static readonly int[] AltKeys = { 0x12, 0xA4, 0xA5 };    // VK_MENU, VK_LMENU, VK_RMENU
    private static readonly int[] WinKeys = { 0x5B, 0x5C };          // VK_LWIN, VK_RWIN
    private static readonly int[] AltGrKeys = { 0xA5 };              // VK_RMENU (AltGr reports as right Alt)

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private IntPtr _hookId = IntPtr.Zero;
    // keep a reference so the delegate isn't garbage-collected while the hook is installed
    private readonly LowLevelKeyboardProc _proc;
    private readonly HashSet<int> _pressedKeys = new();
    private bool _comboActive;

    /// <summary>Which modifier combo currently triggers push-to-talk.</summary>
    public HotkeyPreset Preset { get; set; } = HotkeyPreset.CtrlShift;

    public event Action? HotkeyPressed;   // combo became active (start recording)
    public event Action? HotkeyReleased;  // combo released (stop recording)

    public GlobalHotkeyService()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            bool isDownMsg = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            bool isUpMsg = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;

            if (isDownMsg) _pressedKeys.Add(vkCode);
            else if (isUpMsg) _pressedKeys.Remove(vkCode);

            if (isDownMsg || isUpMsg)
            {
                bool active = IsComboActive();
                if (active && !_comboActive)
                {
                    _comboActive = true;
                    HotkeyPressed?.Invoke();
                }
                else if (!active && _comboActive)
                {
                    _comboActive = false;
                    HotkeyReleased?.Invoke();
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool IsComboActive() => Preset switch
    {
        HotkeyPreset.CtrlShift => AnyPressed(CtrlKeys) && AnyPressed(ShiftKeys),
        HotkeyPreset.CtrlWin => AnyPressed(CtrlKeys) && AnyPressed(WinKeys),
        HotkeyPreset.CtrlAlt => AnyPressed(CtrlKeys) && AnyPressed(AltKeys),
        HotkeyPreset.WinShift => AnyPressed(WinKeys) && AnyPressed(ShiftKeys),
        HotkeyPreset.WinAlt => AnyPressed(WinKeys) && AnyPressed(AltKeys),
        HotkeyPreset.AltGr => AnyPressed(AltGrKeys),
        _ => false
    };

    private bool AnyPressed(int[] keys)
    {
        foreach (var k in keys)
            if (_pressedKeys.Contains(k)) return true;
        return false;
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
