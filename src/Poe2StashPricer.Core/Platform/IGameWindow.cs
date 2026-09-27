using System.Drawing;

namespace Poe2StashPricer.Platform;

/// <summary>
/// The Path of Exile 2 window. Under Proton the game is an XWayland (X11) client on every compositor,
/// so it can be found and measured through X11 wherever it runs.
/// </summary>
public interface IGameWindow
{
    /// <summary>Looks for the game window. True when it was found; the other members need it.</summary>
    bool Find();

    /// <summary>The window's drawable area in screen coordinates (physical pixels), empty when unknown.</summary>
    Rectangle ClientRectOnScreen();

    /// <summary>True while the game has the keyboard focus. A scan only runs then.</summary>
    bool IsForeground();
}
