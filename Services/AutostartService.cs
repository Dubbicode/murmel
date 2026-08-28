using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Murmel.Services;

/// <summary>
/// Registers/unregisters Murmel to launch automatically when Windows starts, via the
/// standard per-user "Run" registry key. This works for a plain portable .exe - no
/// installer, no admin rights needed, since it's written under HKEY_CURRENT_USER.
/// The registered command includes "--minimized" so an autostart launch comes up
/// quietly in the background (tray + small indicator) instead of popping the window
/// open at every login.
/// </summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Murmel";
    public const string MinimizedStartupArg = "--minimized";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;
            key.SetValue(ValueName, $"\"{exePath}\" {MinimizedStartupArg}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
