using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NotchWin.App.Interop;
using Windows.System;

namespace NotchWin.App.Views;

/// <summary>
/// WinMac 2.0 — keyboard in the open Island (docs/25). The Island never takes the keyboard by itself (hover, notifications, the pill): only when the user
/// opens it with a click it becomes focusable for that session, and it gives the keyboard back as soon as it closes. Keys are handled only while it has
/// the focus — nothing is intercepted globally.
///   Esc close · ←/→ previous/next section (from the tab bar) · Ctrl+1…9 section N · Tab / Mayús+Tab and ↑/↓ move between cards and actions ·
///   Intro/Espacio activate (standard buttons) · focus shown with the system focus rectangle.
/// </summary>
public sealed partial class NotchWindow
{
    private bool _keyboardOn;

    private void InitKeyboard()
    {
        RootGrid.KeyDown += OnIslandKey;
        RootGrid.TabFocusNavigation = KeyboardNavigationMode.Cycle;   // Tab stays inside the Island while it has the keyboard
    }

    /// <summary>Called after the user opened the Island with a click: let it take the keyboard and focus the selected section's tab.</summary>
    private void KeyboardOn()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var ex = (uint)NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex & ~NativeMethods.WS_EX_NOACTIVATE));
            NativeMethods.SetForegroundWindow(hwnd);   // allowed: this process just received the user's click
            _keyboardOn = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FocusActiveTab(FocusState.Programmatic));
        }
        catch (Exception) { }
    }

    /// <summary>The Island closed: back to never taking the keyboard.</summary>
    private void KeyboardOff()
    {
        if (!_keyboardOn) return;
        _keyboardOn = false;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var ex = (uint)NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex | NativeMethods.WS_EX_NOACTIVATE));
        }
        catch (Exception) { }
    }

    private IReadOnlyList<string> EnabledSectionIds() =>
        _viewModel.Sections.Where(s => s.Enabled && SectionInfo.ContainsKey(s.Id)).Select(s => s.Id).ToList();

    private void SelectSection(string id)
    {
        if (id == _active) return;
        _active = id; _customizing = false;
        RebuildTabs(); ShowActivePane(); RelayoutIfExpanded();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FocusActiveTab(FocusState.Keyboard));
        SelfTestKey("seccion " + id);
    }

    private void FocusActiveTab(FocusState how)
    {
        int i = _visibleTabIds.ToList().IndexOf(_active);
        var tabs = TabBar.Children.OfType<Button>().ToList();
        if (i >= 0 && i < tabs.Count) tabs[i].Focus(how);
        else if (tabs.Count > 0) tabs[^1].Focus(how);   // a section shown through «Más»: the «Más» button
    }

    private void OnIslandKey(object sender, KeyRoutedEventArgs e)
    {
        if (!_expanded) return;
        bool inText = e.OriginalSource is TextBox;
        bool ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        bool onTabs = e.OriginalSource is DependencyObject d && IsInside(d, TabBar);
        var ids = EnabledSectionIds(); int cur = ids.ToList().IndexOf(_active);
        switch (e.Key)
        {
            case VirtualKey.Escape: SetExpanded(false); SelfTestKey("esc"); e.Handled = true; return;
            case >= VirtualKey.Number1 and <= VirtualKey.Number9 when ctrl:
                int n = e.Key - VirtualKey.Number1; if (n < ids.Count) SelectSection(ids[n]); e.Handled = true; return;
            case VirtualKey.Left or VirtualKey.Right when !inText && (onTabs || ctrl) && ids.Count > 0:
                SelectSection(ids[(Math.Max(0, cur) + (e.Key == VirtualKey.Right ? 1 : ids.Count - 1)) % ids.Count]); e.Handled = true; return;
            case VirtualKey.Down when !inText:
                e.Handled = FocusManager.TryMoveFocus(FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = Content.XamlRoot.Content }); return;
            case VirtualKey.Up when !inText && !onTabs:
                e.Handled = FocusManager.TryMoveFocus(FocusNavigationDirection.Previous, new FindNextElementOptions { SearchRoot = Content.XamlRoot.Content }); return;
        }
    }

    private static bool IsInside(DependencyObject d, DependencyObject root)
    {
        for (var x = d; x is not null; x = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(x)) if (ReferenceEquals(x, root)) return true;
        return false;
    }

    /// <summary>CI only (WINMAC_PILL_SELFTEST): what the keyboard did, so the audit can verify it with real key presses.</summary>
    private void SelfTestKey(string what)
    {
        if (string.IsNullOrEmpty(SelfTestPath)) return;
        try { File.AppendAllText(SelfTestPath!, $"{DateTime.UtcNow:HH:mm:ss.fff} teclado {what} active={_active} expanded={_expanded}{Environment.NewLine}"); } catch (Exception) { }
    }
}
