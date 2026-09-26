using Poe2StashPricer.Platform;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>
/// What the user is holding, asked of the X server rather than of our own window: XQueryKeymap returns the
/// whole keyboard and XQueryPointer the modifier and button mask, both of which stay right while the game
/// has the focus (measured on Hyprland with the game in front).
/// </summary>
internal sealed class X11KeyState : IKeyState
{
    private const uint ShiftMask = 1 << 0, ControlMask = 1 << 2, Mod1Mask = 1 << 3;   // Mod1 is Alt
    private const uint ButtonMask = 0x1f00;                                           // buttons 1-5

    private readonly X11Display display;
    private readonly byte[] keys = new byte[32];

    public X11KeyState(X11Display display)
    {
        this.display = display;
    }

    /// <summary>The keycode of the scan hotkey, so holding it can be waited out; 0 when it is not bound here.</summary>
    public int HotkeyCode { get; set; }

    public bool AbortPressed => IsDown(KeyCode("Escape"));

    public bool ScanTriggerHeld => (HotkeyCode != 0 && IsDown(HotkeyCode)) || ModifiersHeld;

    public bool ModifiersHeld => (PointerMask() & (ShiftMask | ControlMask | Mod1Mask)) != 0;

    public bool MouseButtonHeld => (PointerMask() & ButtonMask) != 0;

    public int KeyCode(string keysymName)
    {
        lock (display.Sync)
        {
            ulong sym = X11.XStringToKeysym(keysymName);
            return sym == 0 ? 0 : (int)X11.XKeysymToKeycode(display.Handle, sym);
        }
    }

    private bool IsDown(int keycode)
    {
        if (keycode <= 0 || keycode > 255) return false;
        lock (display.Sync)
        {
            X11.XQueryKeymap(display.Handle, keys);
            return (keys[keycode / 8] & (1 << (keycode % 8))) != 0;
        }
    }

    private uint PointerMask()
    {
        lock (display.Sync)
        {
            if (!X11.XQueryPointer(display.Handle, display.Root, out _, out _, out _, out _, out _, out _, out uint mask))
                return 0;
            return mask;
        }
    }
}
