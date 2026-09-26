namespace Poe2StashPricer.Platform;

/// <summary>
/// The system clipboard. <see cref="ChangeCount"/> is what makes a scan quick: after Ctrl+C the scanner
/// waits for the count to move, which tells it the game answered even when the copied text is the same
/// as last time. It must be a real counter of clipboard changes, not a comparison of the text.
/// </summary>
public interface IClipboard
{
    /// <summary>The clipboard's text, or null when it holds none.</summary>
    string GetText();

    /// <param name="sensitive">
    /// The text is the user's own clipboard being put back and may be a password: offer it in a way that
    /// keeps clipboard managers from storing it.
    /// </param>
    void SetText(string text, bool sensitive);

    /// <summary>Goes up on every clipboard change, whatever was copied.</summary>
    ulong ChangeCount { get; }
}
