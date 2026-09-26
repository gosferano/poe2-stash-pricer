using System.Drawing;

namespace Poe2StashPricer.Platform
{
    /// <summary>
    /// Drives the mouse and the keyboard while a scan runs. The game must see real input events (it ignores
    /// mere cursor warps), so the implementation sends them through a virtual device.
    /// </summary>
    public interface IInput
    {
        /// <summary>Moves the cursor to a point in screen coordinates (physical pixels).</summary>
        void MoveMouse(int x, int y);

        Point GetCursorPos();

        /// <summary>Presses Ctrl+C: what copies the hovered item's text in the game.</summary>
        void SendCopy();
    }
}
