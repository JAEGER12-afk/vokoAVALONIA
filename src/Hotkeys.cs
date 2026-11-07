using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace VokoAvalonia;

/// <summary>
/// Global keyboard shortcuts: they work even when another app is in front,
/// as long as this app is running. Windows only (uses RegisterHotKey from user32.dll).
/// On other systems Add() simply returns false.
///
/// Every shortcut is Ctrl + Alt + one letter.
/// </summary>
public class Hotkeys
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x0001;
    const uint MOD_CONTROL = 0x0002;
    const uint MOD_NOREPEAT = 0x4000;   // holding the keys down fires only once

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    readonly IntPtr windowHandle;
    readonly Dictionary<int, Action> actions = new();

    /// <summary>Call this after the window is opened.</summary>
    public Hotkeys(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;

        windowHandle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        // Windows tells the window "a hotkey was pressed"; this lets us listen for that message.
        Win32Properties.AddWndProcHookCallback(window, OnWindowMessage);
    }

    /// <summary>
    /// Registers Ctrl + Alt + letter. Returns false if it could not be registered,
    /// usually because another app already uses that combination.
    /// </summary>
    public bool Add(string letter, Action action)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero || letter.Length != 1)
            return false;

        int id = actions.Count + 1;
        uint key = char.ToUpperInvariant(letter[0]);   // for A-Z the Windows key code is the letter itself
        if (!RegisterHotKey(windowHandle, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, key))
            return false;

        actions[id] = action;
        return true;
    }

    /// <summary>Removes all shortcuts (before registering new ones, and when the app closes).</summary>
    public void Clear()
    {
        foreach (int id in actions.Keys) UnregisterHotKey(windowHandle, id);
        actions.Clear();
    }

    IntPtr OnWindowMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WM_HOTKEY && actions.TryGetValue((int)wParam, out Action? action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }
}
