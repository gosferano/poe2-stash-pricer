using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>
/// A virtual keyboard and absolute pointer created through /dev/uinput. The kernel presents them as real
/// devices, so the game cannot tell them from hardware and no compositor protocol is involved: the same
/// code drives the mouse on Hyprland, KWin or anywhere else.
/// </summary>
internal sealed class UinputDevice : IDisposable
{
    private const string Libc = "libc";

    [DllImport(Libc, SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport(Libc, SetLastError = true)] private static extern int close(int fd);
    [DllImport(Libc, SetLastError = true)] private static extern nint write(int fd, byte[] buf, nint count);
    [DllImport(Libc, SetLastError = true, EntryPoint = "ioctl")] private static extern int ioctl_int(int fd, ulong request, int arg);
    [DllImport(Libc, SetLastError = true, EntryPoint = "ioctl")] private static extern int ioctl_ptr(int fd, ulong request, byte[] arg);

    private const int O_WRONLY = 1, O_NONBLOCK = 2048;

    // _IOW('U', nr, size) on x86_64: dir(1) << 30 | size << 16 | 'U' << 8 | nr
    private const ulong UI_SET_EVBIT = 0x40045564, UI_SET_KEYBIT = 0x40045565, UI_SET_ABSBIT = 0x40045567;
    private const ulong UI_DEV_SETUP = 0x405c5503, UI_ABS_SETUP = 0x401c5504;
    private const ulong UI_DEV_CREATE = 0x5501, UI_DEV_DESTROY = 0x5502;

    private const ushort EV_SYN = 0x00, EV_KEY = 0x01, EV_ABS = 0x03;
    private const ushort SYN_REPORT = 0, ABS_X = 0x00, ABS_Y = 0x01, BTN_LEFT = 0x110;

    public const ushort KEY_LEFTCTRL = 29, KEY_C = 46;

    /// <summary>
    /// A uinput device's absolute range is fixed when the device is made, so it cannot be the screen size:
    /// plugging in a monitor or changing the resolution would leave it stale. The axes span a constant
    /// range instead and every move is scaled into it against the desktop as it is at that moment, which is
    /// what upstream did with SM_CXVIRTUALSCREEN and SendInput's absolute flag.
    /// </summary>
    private const int AbsMax = 65535;

    private readonly object _sync = new object();
    private readonly Func<System.Drawing.Size> _screenSize;
    private int _fd = -1;

    public UinputDevice(Func<System.Drawing.Size> screenSize)
    {
        _screenSize = screenSize;
        _fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK);
        if (_fd < 0)
            throw new InvalidOperationException(
                "cannot open /dev/uinput (" + Errno() + "). The app moves the mouse through a virtual device; "
                + "see the README for the one-time udev rule that grants access.");
        try
        {
            Ioctl(UI_SET_EVBIT, EV_KEY);
            Ioctl(UI_SET_EVBIT, EV_ABS);
            Ioctl(UI_SET_EVBIT, EV_SYN);
            Ioctl(UI_SET_KEYBIT, BTN_LEFT);          // without a button it is not taken for a pointer
            for (int key = 1; key < 128; key++) Ioctl(UI_SET_KEYBIT, key);
            Ioctl(UI_SET_ABSBIT, ABS_X);
            Ioctl(UI_SET_ABSBIT, ABS_Y);
            AbsSetup(ABS_X);
            AbsSetup(ABS_Y);
            DevSetup("poe2-stash-pricer");
            if (ioctl_int(_fd, UI_DEV_CREATE, 0) < 0)
                throw new InvalidOperationException("the virtual input device could not be created (" + Errno() + ")");
            // The compositor has to notice the new device before it will route anything from it.
            Thread.Sleep(400);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static string Errno() { return "errno " + Marshal.GetLastWin32Error(); }

    private void Ioctl(ulong request, int arg)
    {
        if (ioctl_int(_fd, request, arg) < 0)
            throw new InvalidOperationException("uinput setup failed (" + Errno() + ")");
    }

    private void AbsSetup(ushort code)
    {
        // struct uinput_abs_setup { __u16 code; struct input_absinfo absinfo; }, absinfo = value, min, max, fuzz, flat, res
        byte[] b = new byte[28];
        BitConverter.GetBytes(code).CopyTo(b, 0);
        BitConverter.GetBytes(0).CopyTo(b, 4);
        BitConverter.GetBytes(0).CopyTo(b, 8);
        BitConverter.GetBytes(AbsMax).CopyTo(b, 12);
        if (ioctl_ptr(_fd, UI_ABS_SETUP, b) < 0)
            throw new InvalidOperationException("uinput axis setup failed (" + Errno() + ")");
    }

    private void DevSetup(string name)
    {
        // struct uinput_setup { struct input_id id; char name[80]; __u32 ff_effects_max; }
        byte[] b = new byte[92];
        BitConverter.GetBytes((ushort)0x03).CopyTo(b, 0);     // BUS_USB
        BitConverter.GetBytes((ushort)0x1209).CopyTo(b, 2);
        BitConverter.GetBytes((ushort)0x0001).CopyTo(b, 4);
        BitConverter.GetBytes((ushort)0x0001).CopyTo(b, 6);
        byte[] n = System.Text.Encoding.ASCII.GetBytes(name);
        Array.Copy(n, 0, b, 8, Math.Min(n.Length, 79));
        if (ioctl_ptr(_fd, UI_DEV_SETUP, b) < 0)
            throw new InvalidOperationException("uinput device setup failed (" + Errno() + ")");
    }

    private void Emit(ushort type, ushort code, int value)
    {
        // struct input_event { struct timeval time; __u16 type; __u16 code; __s32 value; }; the kernel fills in the time.
        byte[] ev = new byte[24];
        BitConverter.GetBytes(type).CopyTo(ev, 16);
        BitConverter.GetBytes(code).CopyTo(ev, 18);
        BitConverter.GetBytes(value).CopyTo(ev, 20);
        if (write(_fd, ev, 24) != 24)
            throw new InvalidOperationException("writing to the virtual device failed (" + Errno() + ")");
    }

    private void Sync() { Emit(EV_SYN, SYN_REPORT, 0); }

    public void MoveMouse(int x, int y)
    {
        lock (_sync)
        {
            System.Drawing.Size screen = _screenSize();
            Emit(EV_ABS, ABS_X, Scale(x, screen.Width));
            Emit(EV_ABS, ABS_Y, Scale(y, screen.Height));
            Sync();
        }
    }

    private static int Scale(int v, int extent)
    {
        return Math.Clamp((int)Math.Round(v * (double)AbsMax / Math.Max(1, extent - 1)), 0, AbsMax);
    }

    public void SendCopy()
    {
        lock (_sync)
        {
            Emit(EV_KEY, KEY_LEFTCTRL, 1);
            Sync();
            Emit(EV_KEY, KEY_C, 1);
            Sync();
            Emit(EV_KEY, KEY_C, 0);
            Sync();
            Emit(EV_KEY, KEY_LEFTCTRL, 0);
            Sync();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_fd < 0) return;
            ioctl_int(_fd, UI_DEV_DESTROY, 0);
            close(_fd);
            _fd = -1;
        }
    }
}
