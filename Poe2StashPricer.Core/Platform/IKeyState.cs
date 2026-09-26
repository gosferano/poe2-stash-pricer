namespace Poe2StashPricer.Platform;

/// <summary>
/// What the user is holding down right now. Upstream asked Windows per virtual key
/// (<c>GetAsyncKeyState</c>); on Wayland only the focused window is told about keys, so this is a small
/// set of questions the scan actually needs instead of a general "is this key down".
/// </summary>
public interface IKeyState
{
    /// <summary>The user asked for the running scan to stop (Esc, or the scan hotkey pressed again).</summary>
    bool AbortPressed { get; }

    /// <summary>
    /// The scan hotkey, or one of its modifiers, is still held. A scan waits for this to go away before
    /// it starts: its own Ctrl+C would otherwise mix with the key the user is still pressing.
    /// </summary>
    bool ScanTriggerHeld { get; }

    /// <summary>Ctrl, Alt or Shift is held. Hover pricing doesn't copy then (the user is doing something).</summary>
    bool ModifiersHeld { get; }

    /// <summary>A mouse button is held (an item is being dragged): hover pricing waits.</summary>
    bool MouseButtonHeld { get; }
}
