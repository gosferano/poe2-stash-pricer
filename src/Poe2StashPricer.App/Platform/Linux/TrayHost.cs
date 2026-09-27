using System;
using System.Threading.Tasks;
using Poe2StashPricer.Storage;
using Tmds.DBus;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>The bus itself, which knows who is on it.</summary>
[DBusInterface("org.freedesktop.DBus")]
public interface IDBusNames : IDBusObject
{
    Task<bool> NameHasOwnerAsync(string name);
}

/// <summary>
/// Whether the desktop has anywhere to put a tray icon. Whatever shows one owns
/// org.kde.StatusNotifierWatcher on the session bus: Plasma does, and so do the Wayland panels that
/// implement the same protocol. It matters because without one an icon goes nowhere, and an app that
/// hides its window instead of closing would then have no way back.
/// </summary>
internal static class TrayHost
{
    private const string Watcher = "org.kde.StatusNotifierWatcher";

    public static bool IsPresent()
    {
        try
        {
            Task<bool> asking = Task.Run(AskAsync);
            return asking.Wait(1500) && asking.Result;
        }
        catch (Exception ex)
        {
            Log.Write("cannot tell whether the desktop has a tray: " + ex.Message);
            return false;
        }
    }

    private static async Task<bool> AskAsync()
    {
        using Connection connection = new Connection(Address.Session!);
        await connection.ConnectAsync();
        IDBusNames bus = connection.CreateProxy<IDBusNames>("org.freedesktop.DBus", "/org/freedesktop/DBus");
        return await bus.NameHasOwnerAsync(Watcher);
    }
}
