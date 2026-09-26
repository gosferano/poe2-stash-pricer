using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>
/// The parts of libX11 and XFixes the app uses. The game runs under Proton, so it is an XWayland (X11)
/// client on every compositor: everything here works the same on Hyprland, KWin or a plain X session.
/// </summary>
internal static class X11
{
    private const string Xlib = "libX11.so.6";
    private const string Xfixes = "libXfixes.so.3";

    public const int ZPixmap = 2;
    public const ulong AllPlanes = ulong.MaxValue;
    public const int IsViewable = 2;
    public const int PropModeReplace = 0;
    public const int SelectionNotify = 31, SelectionRequest = 30, SelectionClear = 29;

    public const long PropertyChangeMask = 1L << 22;
    public const ulong XFixesSetSelectionOwnerNotifyMask = 1;

    // Atoms that are always defined, so they need no round trip to name.
    public const ulong XA_ATOM = 4, XA_CARDINAL = 6, XA_STRING = 31, XA_WINDOW = 33;

    [DllImport(Xlib)] public static extern IntPtr XOpenDisplay(string? name);
    [DllImport(Xlib)] public static extern int XCloseDisplay(IntPtr d);
    [DllImport(Xlib)] public static extern IntPtr XDefaultRootWindow(IntPtr d);
    [DllImport(Xlib)] public static extern ulong XInternAtom(IntPtr d, string name, bool onlyIfExists);
    [DllImport(Xlib)] public static extern int XFree(IntPtr data);
    [DllImport(Xlib)] public static extern int XSync(IntPtr d, bool discard);
    [DllImport(Xlib)] public static extern int XFlush(IntPtr d);
    [DllImport(Xlib)] public static extern bool XQueryExtension(IntPtr d, string name, out int major, out int firstEvent, out int firstError);
    [DllImport(Xlib)] public static extern int XGetWindowAttributes(IntPtr d, IntPtr w, out XWindowAttributes a);
    [DllImport(Xlib)] public static extern bool XTranslateCoordinates(IntPtr d, IntPtr src, IntPtr dst, int srcX, int srcY, out int dstX, out int dstY, out IntPtr child);
    [DllImport(Xlib)] public static extern IntPtr XGetImage(IntPtr d, IntPtr drawable, int x, int y, uint w, uint h, ulong planeMask, int format);
    [DllImport(Xlib)] public static extern int XQueryKeymap(IntPtr d, byte[] keys);
    [DllImport(Xlib)] public static extern bool XQueryPointer(IntPtr d, IntPtr w, out IntPtr root, out IntPtr child,
                                                              out int rootX, out int rootY, out int winX, out int winY, out uint mask);
    [DllImport(Xlib)] public static extern IntPtr XSetErrorHandler(ErrorHandler? handler);
    [DllImport(Xlib)] public static extern int XGetErrorText(IntPtr d, int code, StringBuilder buf, int len);
    [DllImport(Xlib)] public static extern IntPtr XKeysymToKeycode(IntPtr d, ulong keysym);
    [DllImport(Xlib)] public static extern ulong XStringToKeysym(string name);

    [DllImport(Xlib)]
    public static extern int XGetWindowProperty(IntPtr d, IntPtr w, ulong property, long offset, long length, bool delete,
                                                ulong reqType, out ulong actualType, out int actualFormat,
                                                out ulong nitems, out ulong bytesAfter, out IntPtr prop);

    [DllImport(Xlib)] public static extern int XChangeProperty(IntPtr d, IntPtr w, ulong property, ulong type, int format,
                                                               int mode, byte[] data, int nelements);
    [DllImport(Xlib)] public static extern int XDeleteProperty(IntPtr d, IntPtr w, ulong property);

    [DllImport(Xlib)] public static extern IntPtr XCreateSimpleWindow(IntPtr d, IntPtr parent, int x, int y, uint w, uint h,
                                                                      uint borderWidth, ulong border, ulong background);
    [DllImport(Xlib)] public static extern int XDestroyWindow(IntPtr d, IntPtr w);
    [DllImport(Xlib)] public static extern int XSelectInput(IntPtr d, IntPtr w, long mask);
    [DllImport(Xlib)] public static extern int XPending(IntPtr d);
    [DllImport(Xlib)] public static extern int XNextEvent(IntPtr d, byte[] ev);
    [DllImport(Xlib)] public static extern int XSendEvent(IntPtr d, IntPtr w, bool propagate, long mask, byte[] ev);
    [DllImport(Xlib)] public static extern int XConvertSelection(IntPtr d, ulong selection, ulong target, ulong property, IntPtr requestor, ulong time);
    [DllImport(Xlib)] public static extern int XSetSelectionOwner(IntPtr d, ulong selection, IntPtr owner, ulong time);
    [DllImport(Xlib)] public static extern IntPtr XGetSelectionOwner(IntPtr d, ulong selection);
    [DllImport(Xlib)] public static extern int XConnectionNumber(IntPtr d);

    [DllImport(Xfixes)] public static extern bool XFixesQueryExtension(IntPtr d, out int eventBase, out int errorBase);
    [DllImport(Xfixes)] public static extern void XFixesSelectSelectionInput(IntPtr d, IntPtr w, ulong selection, ulong eventMask);

    public delegate int ErrorHandler(IntPtr display, IntPtr errorEvent);

    [StructLayout(LayoutKind.Sequential)]
    public struct XWindowAttributes
    {
        public int x, y, width, height, border_width, depth;
        public IntPtr visual;
        public IntPtr root;
        public int @class, bit_gravity, win_gravity, backing_store;
        public ulong backing_planes, backing_pixel;
        public int save_under;
        public IntPtr colormap;
        public int map_installed, map_state;
        public long all_event_masks, your_event_mask, do_not_propagate_mask;
        public int override_redirect;
        public IntPtr screen;
    }

    /// <summary>XImage as far as the destroy hook, which is the second function pointer in its f struct.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XImage
    {
        public int width, height, xoffset, format;
        public IntPtr data;
        public int byte_order, bitmap_unit, bitmap_bit_order, bitmap_pad, depth, bytes_per_line, bits_per_pixel;
        public ulong red_mask, green_mask, blue_mask;
        public IntPtr obdata;
        public IntPtr f_create_image;
        public IntPtr f_destroy_image;
    }

    private delegate int DestroyImageFn(IntPtr image);

    /// <summary>XDestroyImage is a macro in C: it calls the image's own destructor.</summary>
    public static void DestroyImage(IntPtr image)
    {
        XImage img = Marshal.PtrToStructure<XImage>(image);
        Marshal.GetDelegateForFunctionPointer<DestroyImageFn>(img.f_destroy_image)(image);
    }
}
