using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Input.Platform;

namespace Murmel.Services;

/// <summary>
/// Injects text into whatever window currently has focus (the "active" text field),
/// so the app can run in the background without manual copy/paste.
///
/// Approach: remember the foreign foreground window handle (captured by GlobalHotkeyService
/// right when the hotkey was pressed), put the text on the clipboard, make sure that window
/// is foreground again, then simulate Ctrl+V. This is the same technique tools like
/// WisprFlow/Superwhisper use, because it works in essentially every app (browsers, Office,
/// Electron apps, games with text fields, etc.) without needing per-app integration.
/// </summary>
public static class TextInjector
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static IntPtr CaptureCurrentForegroundWindow() => GetForegroundWindow();

    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard, restores focus to
    /// <paramref name="targetWindow"/> (the window that was active before recording started),
    /// then simulates Ctrl+V to paste it there.
    /// </summary>
    public static async System.Threading.Tasks.Task InjectAsync(IClipboard? clipboard, string text, IntPtr targetWindow)
    {
        if (string.IsNullOrEmpty(text) || clipboard is null) return;

        await clipboard.SetTextAsync(text);

        if (targetWindow != IntPtr.Zero)
        {
            SetForegroundWindow(targetWindow);
            // give the OS a moment to actually switch focus before we send keystrokes
            await System.Threading.Tasks.Task.Delay(60);
        }

        SendCtrlV();
    }

    private static void SendCtrlV()
    {
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
