namespace Poe2StashPricer.Storage;

/// <summary>
/// A hotkey as a key name plus modifiers, e.g. "F7" or Ctrl+Shift+S. Upstream stored a WinForms Keys
/// value and registered the hotkey itself; here the key is bound outside the app (the desktop's global
/// shortcuts), so all that is kept is a name to show and to write to the settings file.
/// </summary>
public sealed record Hotkey(string Key, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
    public override string ToString()
    {
        string s = "";
        if (Ctrl) s += "Ctrl+";
        if (Alt) s += "Alt+";
        if (Shift) s += "Shift+";
        return s + Key;
    }
}
