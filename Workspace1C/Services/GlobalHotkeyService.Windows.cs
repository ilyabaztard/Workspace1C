#if WINDOWS
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Workspace1C.Services;

/// <summary>
/// Глобальная горячая клавиша «показать главное окно» на Windows (issue #82):
/// RegisterHotKey (user32) на дескрипторе главного окна + обработка WM_HOTKEY.
/// Регистрация переживает деактивацию приложения и работает, когда окно в трее,
/// свёрнуто или на заднем плане. Дерегистрируется при Dispose (закрытии окна);
/// повторный SetGesture перерегистрирует клавишу без перезапуска.
/// </summary>
public sealed class GlobalHotkeyServiceWindows : IGlobalHotkeyService
{
    private const int WmHotkey = 0x0312;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    // Идентификатор сервиса — единственная глобальная клавиша приложения.
    private const int HotkeyId = 0xC0DE;

    private readonly IntPtr _hwnd;
    private HwndSource? _source;
    private string? _gesture;

    /// <summary>Создаёт сервис на основе дескриптора главного окна (зарегистрировано не сразу).</summary>
    public GlobalHotkeyServiceWindows(Window window)
    {
        if (window is null) throw new ArgumentNullException(nameof(window));
        _hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    public string? RegisteredGesture => _gesture;

    public Action? HotkeyPressed { get; set; }

    public void SetGesture(string? gesture)
    {
        UnregisterCore();
        _gesture = GlobalHotkeyLogic.Normalize(gesture);
        if (_gesture is null || _gesture.Length == 0)
        {
            _gesture = null;
            return;
        }

        if (!HotkeyMatcher.TryParse(_gesture, out var keyName, out var modifiers))
        {
            _gesture = null; // невалидное значение настройки — не регистрируем
            return;
        }

        var vk = KeyNameToVirtualKey(keyName);
        if (vk == 0)
        {
            _gesture = null;
            return;
        }

        // Конфликт с другой программой (RegisterHotKey=false): клавиша не сработает,
        // но приложение обязано продолжать работать.
        if (!RegisterHotKey(_hwnd, HotkeyId, ToUser32Modifiers(modifiers), vk))
            _gesture = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void UnregisterCore()
    {
        if (_gesture is null)
            return;
        try
        {
            UnregisterHotKey(_hwnd, HotkeyId);
        }
        catch
        {
            // Окно могло быть закрыто — скрывать дефект регистрации нельзя, но
            // бросать здесь некуда: просто перестаём считать клавишу действующей.
        }
        finally
        {
            _gesture = null;
        }
    }

    public void Dispose()
    {
        UnregisterCore();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
        HotkeyPressed = null;
        GC.SuppressFinalize(this);
    }

    private static uint ToUser32Modifiers(HotkeyModifiers modifiers)
    {
        uint result = 0;
        if ((modifiers & HotkeyModifiers.Alt) != 0) result |= ModAlt;
        if ((modifiers & HotkeyModifiers.Control) != 0) result |= ModControl;
        if ((modifiers & HotkeyModifiers.Shift) != 0) result |= ModShift;
        if ((modifiers & HotkeyModifiers.Windows) != 0) result |= ModWin;
        return result;
    }

    /// <summary>
    /// Имя физической клавиши HotkeyMatcher (Key.ToString в WPF) → виртуальный код
    /// user32. Буквы/цифры совпадают с ASCII-кодами VK; для них раскладка не важна
    /// (глобальные сочетания типа Ctrl+Alt+W регистрируются по VK-коду).
    /// </summary>
    private static uint KeyNameToVirtualKey(string keyName)
    {
        var upper = keyName.ToUpperInvariant();
        switch (upper)
        {
            case "F1": return 0x70; case "F2": return 0x71; case "F3": return 0x72;
            case "F4": return 0x73; case "F5": return 0x74; case "F6": return 0x75;
            case "F7": return 0x76; case "F8": return 0x77; case "F9": return 0x78;
            case "F10": return 0x79; case "F11": return 0x7A; case "F12": return 0x7B;
            case "F13": return 0x7C; case "F14": return 0x7D; case "F15": return 0x7E;
            case "F16": return 0x7F; case "F17": return 0x80; case "F18": return 0x81;
            case "F19": return 0x82; case "F20": return 0x83; case "F21": return 0x84;
            case "F22": return 0x85; case "F23": return 0x86; case "F24": return 0x87;
            case "ESC": return 0x1B;
            case "TAB": return 0x09;
            case "ENTER": return 0x0D;
            case "SPACE": return 0x20;
            case "BACK": return 0x08;
            case "DELETE": return 0x2E;
            case "HOME": return 0x24;
            case "END": return 0x23;
            case "INSERT": return 0x2D;
            case "PAGEUP": return 0x21;
            case "PAGEDOWN": return 0x22;
            case "UP": return 0x26;
            case "DOWN": return 0x28;
            case "LEFT": return 0x25;
            case "RIGHT": return 0x27;
            // Буквы и цифры: VK = ASCII-код (A-Z = 0x41-0x5A, 0-9 = 0x30-0x39).
            default:
            {
                if (upper.Length == 1 && char.IsLetterOrDigit(upper[0]))
                    return char.ToUpperInvariant(upper[0]);
                return 0;
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
#endif