using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;
using FortniteCompanion.Core.Services;

namespace FortniteCompanion.Native;

/// <summary>
/// Global klavye kısayolları (RegisterHotKey). Görünmez, yalnızca mesaj alan bir pencere kullanır.
/// Bu yöntem tuşları okumaz veya oyuna tuş göndermez; Windows yalnızca kayıtlı kombinasyon basıldığında
/// bize haber verir. Not: kayıtlı kombinasyon oyuna iletilmez, bu yüzden oyunda kullanılmayan
/// kombinasyonlar seçilmelidir.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 0xB000;

    public HotkeyManager()
    {
        var p = new HwndSourceParameters("FortniteCompanionHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = NativeMethods.HWND_MESSAGE,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>"Ctrl+Alt+O" gibi bir metni çözümler.</summary>
    public static bool TryParse(string? text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        Key? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "win":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    var name = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
                    if (Enum.TryParse<Key>(name, ignoreCase: true, out var k))
                    {
                        key = k;
                    }
                    else
                    {
                        return false;
                    }
                    break;
            }
        }

        if (key is not Key finalKey || modifiers == 0)
        {
            return false; // tek başına tuş, oyunda yanlışlıkla tetiklenmesin
        }
        vk = (uint)KeyInterop.VirtualKeyFromKey(finalKey);
        return vk != 0;
    }

    /// <summary>Kısayolu kaydeder. Başka bir program kullanıyorsa false döner.</summary>
    public bool Register(string? text, Action handler)
    {
        if (!TryParse(text, out var mods, out var vk))
        {
            AppLog.Warn("Geçersiz kısayol: " + text);
            return false;
        }

        var id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, mods | NativeMethods.MOD_NOREPEAT, vk))
        {
            AppLog.Warn($"Kısayol başka bir program tarafından kullanılıyor: {text}");
            return false;
        }
        _handlers[id] = handler;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }
        _handlers.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AppLog.Error("Kısayol işlenemedi", ex);
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.Dispose();
    }
}
