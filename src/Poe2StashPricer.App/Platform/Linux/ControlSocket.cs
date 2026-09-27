using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>
/// A Unix socket the running app listens on, so a second copy started by a key binding can tell it what to
/// do. The desktop binds the key; the app only has to answer.
///
/// This is the plain way to get a hotkey while the game has focus: a compositor will not hand a key to a
/// window that is not focused, but it will happily run a command. The xdg-desktop-portal GlobalShortcuts
/// interface would avoid the binding step, and is the better long-term answer.
/// </summary>
internal sealed class ControlSocket : IDisposable
{
    private readonly string _path;
    private Socket? _listener;
    private Thread? _thread;
    private volatile bool _stop;

    public ControlSocket()
    {
        string runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/tmp";
        _path = Path.Combine(runtime, "poe2-stash-pricer.sock");
    }

    /// <summary>What a command asks for; unknown ones are ignored.</summary>
    public event Action<string>? Command;

    public void Listen()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);   // left by a copy that did not shut down cleanly
            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(_path));
            _listener.Listen(4);
            _thread = new Thread(Accept) { IsBackground = true, Name = "control" };
            _thread.Start();
            Log.Write("listening for commands on " + _path);
        }
        catch (Exception ex) { Log.Write("the control socket could not be opened: " + ex.Message); }
    }

    private void Accept()
    {
        while (!_stop)
        {
            try
            {
                Socket client = _listener!.Accept();
                using (client)
                {
                    byte[] buffer = new byte[256];
                    int n = client.Receive(buffer);
                    string command = Encoding.UTF8.GetString(buffer, 0, n).Trim();
                    Log.Write("command: " + command);
                    Action<string>? handler = Command;
                    if (handler != null) handler(command);
                    client.Send(Encoding.UTF8.GetBytes("ok"));
                }
            }
            catch (Exception ex)
            {
                if (!_stop) Log.Write("control socket: " + ex.Message);
                return;
            }
        }
    }

    /// <summary>Sends one command to a running copy. True when something was listening.</summary>
    public static bool Send(string command)
    {
        string runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/tmp";
        string path = Path.Combine(runtime, "poe2-stash-pricer.sock");
        if (!File.Exists(path)) return false;
        try
        {
            using (Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
            {
                socket.Connect(new UnixDomainSocketEndPoint(path));
                socket.Send(Encoding.UTF8.GetBytes(command));
                byte[] buffer = new byte[16];
                socket.Receive(buffer);
                return true;
            }
        }
        catch { return false; }
    }

    public void Dispose()
    {
        _stop = true;
        try { _listener?.Close(); } catch { }
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }
}
