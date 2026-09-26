using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>
/// The parts of libwayland-client the clipboard needs.
///
/// Wayland's C API expects the interface tables that its code generator normally writes out. There is no
/// generated code here, so the few tables we need are built in unmanaged memory by <see cref="WlInterface"/>.
/// Everything is sent through wl_proxy_marshal_array_flags, which takes its arguments as an array: the
/// variadic entry point the C headers use cannot be called safely from here.
/// </summary>
internal static class Wl
{
    private const string Lib = "libwayland-client.so.0";

    [DllImport(Lib)] public static extern IntPtr wl_display_connect(string? name);
    [DllImport(Lib)] public static extern void wl_display_disconnect(IntPtr display);
    [DllImport(Lib)] public static extern int wl_display_roundtrip(IntPtr display);
    [DllImport(Lib)] public static extern int wl_display_dispatch(IntPtr display);
    [DllImport(Lib)] public static extern int wl_display_flush(IntPtr display);
    [DllImport(Lib)] public static extern int wl_display_get_error(IntPtr display);

    [DllImport(Lib)] public static extern IntPtr wl_proxy_marshal_array_flags(IntPtr proxy, uint opcode, IntPtr iface,
                                                                              uint version, uint flags, WlArgument[]? args);
    [DllImport(Lib)] public static extern int wl_proxy_add_listener(IntPtr proxy, IntPtr implementation, IntPtr data);
    [DllImport(Lib)] public static extern void wl_proxy_destroy(IntPtr proxy);
    [DllImport(Lib)] public static extern uint wl_proxy_get_version(IntPtr proxy);

    /// <summary>Core interfaces are exported by the library itself, so only protocol ones must be built.</summary>
    public static IntPtr RegistryInterface => Symbol("wl_registry_interface");
    public static IntPtr SeatInterface => Symbol("wl_seat_interface");

    private const int WL_MARSHAL_FLAG_DESTROY = 1;
    public const uint WL_DISPLAY_GET_REGISTRY = 1;
    public const uint WL_REGISTRY_BIND = 0;

    private static IntPtr _handle;

    private static IntPtr Symbol(string name)
    {
        if (_handle == IntPtr.Zero) _handle = NativeLibrary.Load(Lib);
        return NativeLibrary.GetExport(_handle, name);
    }

    private static readonly List<object> _listeners = new List<object>();

    /// <summary>
    /// Builds a listener for wl_proxy_add_listener. The library keeps the pointer it is given and calls
    /// through it for as long as the proxy lives, so the table cannot be a managed array: the collector
    /// would be free to move it the moment this returns, and the next event would jump into nothing. It is
    /// built in unmanaged memory instead, and the delegates are held here so they outlive it too.
    /// </summary>
    public static IntPtr Listener(params Delegate[] handlers)
    {
        IntPtr block = Marshal.AllocHGlobal(IntPtr.Size * handlers.Length);
        for (int i = 0; i < handlers.Length; i++)
            Marshal.WriteIntPtr(block, IntPtr.Size * i, Marshal.GetFunctionPointerForDelegate(handlers[i]));
        lock (_listeners)
        {
            foreach (Delegate d in handlers) _listeners.Add(d);
            _listeners.Add(block);
        }
        return block;
    }

    public static IntPtr Request(IntPtr proxy, uint opcode, IntPtr newIface, WlArgument[]? args)
    {
        uint version = newIface == IntPtr.Zero ? 0 : wl_proxy_get_version(proxy);
        return wl_proxy_marshal_array_flags(proxy, opcode, newIface, version, 0, args);
    }

    public static void Destructor(IntPtr proxy, uint opcode)
    {
        wl_proxy_marshal_array_flags(proxy, opcode, IntPtr.Zero, wl_proxy_get_version(proxy), WL_MARSHAL_FLAG_DESTROY, null);
    }

    public static IntPtr GetRegistry(IntPtr display)
    {
        return wl_proxy_marshal_array_flags(display, WL_DISPLAY_GET_REGISTRY, RegistryInterface,
                                            wl_proxy_get_version(display), 0, new[] { WlArgument.NewId() });
    }

    /// <summary>registry.bind(name, interface, version) -> a new proxy of that interface.</summary>
    public static IntPtr Bind(IntPtr registry, uint name, WlInterface iface, uint version)
    {
        WlArgument[] args =
        {
            WlArgument.UInt(name),
            WlArgument.Str(iface.NamePtr),
            WlArgument.UInt(version),
            WlArgument.NewId(),
        };
        return wl_proxy_marshal_array_flags(registry, WL_REGISTRY_BIND, iface.Ptr, version, 0, args);
    }
}

/// <summary>union wl_argument: one machine word per argument.</summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
internal struct WlArgument
{
    [FieldOffset(0)] public int i;
    [FieldOffset(0)] public uint u;
    [FieldOffset(0)] public IntPtr p;

    public static WlArgument Int(int v) => new WlArgument { i = v };
    public static WlArgument UInt(uint v) => new WlArgument { u = v };
    public static WlArgument Ptr(IntPtr v) => new WlArgument { p = v };
    public static WlArgument Str(IntPtr utf8) => new WlArgument { p = utf8 };
    public static WlArgument Fd(int fd) => new WlArgument { i = fd };
    public static WlArgument NewId() => new WlArgument { p = IntPtr.Zero };
    public static WlArgument Null() => new WlArgument { p = IntPtr.Zero };
}

/// <summary>
/// One protocol interface built in unmanaged memory: the struct wl_interface, its wl_message arrays and
/// every string they point at. Nothing is ever freed - these live as long as the process, as the generated
/// tables normally would.
/// </summary>
internal sealed class WlInterface
{
    private static readonly List<IntPtr> _kept = new List<IntPtr>();

    public IntPtr Ptr { get; }
    public IntPtr NamePtr { get; }
    public string Name { get; }

    private WlInterface(string name, IntPtr ptr, IntPtr namePtr)
    {
        Name = name;
        Ptr = ptr;
        NamePtr = namePtr;
    }

    /// <summary>Wraps a table the library already exports, so it can be passed to bind like our own.</summary>
    public static WlInterface Wrap(IntPtr ptr, string name)
    {
        return new WlInterface(name, ptr, Marshal.ReadIntPtr(ptr, 0));
    }

    public readonly record struct Message(string Name, string Signature, IntPtr[]? Types);

    /// <summary>
    /// struct wl_interface { const char *name; int version; int method_count; const wl_message *methods;
    /// int event_count; const wl_message *events; }
    /// </summary>
    public static WlInterface Create(string name, int version, Message[] requests, Message[] events)
    {
        IntPtr namePtr = Utf8(name);
        IntPtr iface = Alloc(40);
        Marshal.WriteIntPtr(iface, 0, namePtr);
        Marshal.WriteInt32(iface, 8, version);
        Marshal.WriteInt32(iface, 12, requests.Length);
        Marshal.WriteIntPtr(iface, 16, Messages(requests));
        Marshal.WriteInt32(iface, 24, events.Length);
        Marshal.WriteIntPtr(iface, 32, Messages(events));
        return new WlInterface(name, iface, namePtr);
    }

    /// <summary>struct wl_message { const char *name; const char *signature; const wl_interface **types; }</summary>
    private static IntPtr Messages(Message[] list)
    {
        if (list.Length == 0) return IntPtr.Zero;
        IntPtr block = Alloc(24 * list.Length);
        for (int i = 0; i < list.Length; i++)
        {
            IntPtr at = block + 24 * i;
            Marshal.WriteIntPtr(at, 0, Utf8(list[i].Name));
            Marshal.WriteIntPtr(at, 8, Utf8(list[i].Signature));
            Marshal.WriteIntPtr(at, 16, Types(list[i].Types));
        }
        return block;
    }

    private static IntPtr Types(IntPtr[]? types)
    {
        if (types == null || types.Length == 0) return IntPtr.Zero;
        IntPtr block = Alloc(IntPtr.Size * types.Length);
        for (int i = 0; i < types.Length; i++) Marshal.WriteIntPtr(block, IntPtr.Size * i, types[i]);
        return block;
    }

    private static IntPtr Utf8(string s)
    {
        IntPtr p = Marshal.StringToHGlobalAnsi(s);
        lock (_kept) _kept.Add(p);
        return p;
    }

    private static IntPtr Alloc(int bytes)
    {
        IntPtr p = Marshal.AllocHGlobal(bytes);
        for (int i = 0; i < bytes; i++) Marshal.WriteByte(p, i, 0);
        lock (_kept) _kept.Add(p);
        return p;
    }
}
