using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>
/// A connection to the Wayland compositor, and the globals on it we care about: a seat, and whichever
/// version of the data-control protocol is offered.
/// </summary>
internal sealed class WaylandConnection : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlobalFn(IntPtr data, IntPtr registry, uint name, IntPtr iface, uint version);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlobalRemoveFn(IntPtr data, IntPtr registry, uint name);

    private readonly IntPtr _registryListener;

    public IntPtr Display { get; private set; }
    public IntPtr Registry { get; private set; }

    /// <summary>Every global the compositor advertised, by interface name.</summary>
    public Dictionary<string, (uint Name, uint Version)> Globals { get; } = new Dictionary<string, (uint, uint)>();

    public IntPtr Seat { get; private set; }
    public IntPtr Manager { get; private set; }
    public DataControl? Protocol { get; private set; }

    private WaylandConnection()
    {
        _registryListener = Wl.Listener(new GlobalFn(OnGlobal), new GlobalRemoveFn(OnGlobalRemove));
    }

    public static WaylandConnection? TryOpen()
    {
        WaylandConnection c = new WaylandConnection();
        c.Display = Wl.wl_display_connect(null);
        if (c.Display == IntPtr.Zero)
        {
            Log.Write("no Wayland display (WAYLAND_DISPLAY="
                      + (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "unset") + ")");
            return null;
        }
        c.Registry = Wl.GetRegistry(c.Display);
        Wl.wl_proxy_add_listener(c.Registry, c._registryListener, IntPtr.Zero);
        Wl.wl_display_roundtrip(c.Display);

        // Bind the seat and whichever data-control protocol is there. ext- is the standardised one and wins
        // when both are offered.
        foreach (DataControl protocol in new[] { DataControl.Ext, DataControl.Wlr })
        {
            if (!c.Globals.TryGetValue(protocol.Manager.Name, out (uint Name, uint Version) global)) continue;
            c.Protocol = protocol;
            c.Manager = Wl.Bind(c.Registry, global.Name, protocol.Manager, Math.Min(global.Version, protocol.DeviceVersion));
            break;
        }
        if (c.Globals.TryGetValue("wl_seat", out (uint Name, uint Version) seat))
            c.Seat = Wl.Bind(c.Registry, seat.Name, SeatInterfaceShim, Math.Min(seat.Version, 1u));
        Wl.wl_display_roundtrip(c.Display);
        return c;
    }

    /// <summary>wl_seat's table is exported by the library, so it is wrapped rather than built.</summary>
    private static WlInterface SeatInterfaceShim => _seatShim ??= WlInterface.Wrap(Wl.SeatInterface, "wl_seat");

    private static WlInterface? _seatShim;

    private void OnGlobal(IntPtr data, IntPtr registry, uint name, IntPtr iface, uint version)
    {
        string? n = Marshal.PtrToStringAnsi(iface);
        if (n != null) Globals[n] = (name, version);
    }

    private void OnGlobalRemove(IntPtr data, IntPtr registry, uint name) { }

    public void Dispose()
    {
        if (Display == IntPtr.Zero) return;
        if (Manager != IntPtr.Zero && Protocol != null) Wl.Destructor(Manager, DataControl.ManagerDestroy);
        if (Registry != IntPtr.Zero) Wl.wl_proxy_destroy(Registry);
        Wl.wl_display_disconnect(Display);
        Display = IntPtr.Zero;
    }
}
