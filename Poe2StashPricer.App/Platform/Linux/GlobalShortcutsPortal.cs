using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Poe2StashPricer.Storage;
using Tmds.DBus;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>
/// The desktop's own idea of a shortcut, through xdg-desktop-portal's GlobalShortcuts interface.
///
/// The app never takes a key for itself. It says what shortcuts it has - "scan", "overlay" - and the
/// desktop decides which keys those are and says when one is pressed. That is the whole point: the
/// shortcuts show up in the desktop's own settings, can be rebound there, and survive a config reload,
/// and one piece of code covers every desktop that implements the portal.
/// </summary>
internal sealed class GlobalShortcutsPortal : IAsyncDisposable
{
    private const string Service = "org.freedesktop.portal.Desktop";
    private const string Path = "/org/freedesktop/portal/desktop";

    private Connection? _connection;
    private ObjectPath _session;
    private string _localName = "";
    private IDisposable? _activated;

    /// <summary>The id of a shortcut the user pressed ("scan", "overlay").</summary>
    public event Action<string>? Pressed;

    public bool Ready { get; private set; }

    /// <summary>
    /// Registers the shortcuts. False when there is no portal, which is not a failure: the app falls back
    /// to whatever else the desktop offers.
    /// </summary>
    public async Task<bool> RegisterAsync(IReadOnlyList<(string Id, string Description, string Trigger)> shortcuts)
    {
        try
        {
            _connection = new Connection(Address.Session!);
            ConnectionInfo info = await _connection.ConnectAsync();
            _localName = info.LocalName;

            IGlobalShortcuts portal = _connection.CreateProxy<IGlobalShortcuts>(Service, Path);

            string token = "poe2_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Dictionary<string, object> options = new Dictionary<string, object>
            {
                ["handle_token"] = token,
                ["session_handle_token"] = token,
            };

            // The answer comes back on a Request object whose path can be worked out from the token, so it
            // is listened for before the call is made: a portal may answer faster than a reply returns.
            IDictionary<string, object> result = await Ask(token, () => portal.CreateSessionAsync(options), "CreateSession");
            if (!result.TryGetValue("session_handle", out object? handle)) return false;
            _session = new ObjectPath(handle.ToString()!);

            (string, IDictionary<string, object>)[] wanted = new (string, IDictionary<string, object>)[shortcuts.Count];
            for (int i = 0; i < shortcuts.Count; i++)
                wanted[i] = (shortcuts[i].Id, new Dictionary<string, object>
                {
                    ["description"] = shortcuts[i].Description,
                    ["preferred_trigger"] = shortcuts[i].Trigger,
                });

            await Ask(token + "b", () => portal.BindShortcutsAsync(_session, wanted, "",
                new Dictionary<string, object> { ["handle_token"] = token + "b" }), "BindShortcuts");

            _activated = await portal.WatchActivatedAsync(OnActivated);
            Ready = true;
            Log.Write("global shortcuts registered with the desktop portal");
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("the desktop has no global shortcuts portal: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Makes one portal call and waits for its answer. Every call answers later, on a Request object whose
    /// path is made from the caller's bus name and the token it passed - so the answer is subscribed to
    /// first, and the call made second.
    /// </summary>
    private async Task<IDictionary<string, object>> Ask(string token, Func<Task<ObjectPath>> call, string what)
    {
        string sender = _localName.TrimStart(':').Replace('.', '_');
        ObjectPath expected = new ObjectPath("/org/freedesktop/portal/desktop/request/" + sender + "/" + token);
        TaskCompletionSource<IDictionary<string, object>> done = new TaskCompletionSource<IDictionary<string, object>>();

        IRequest proxy = _connection!.CreateProxy<IRequest>(Service, expected);
        using IDisposable watching = await proxy.WatchResponseAsync(answer =>
        {
            if (answer.response == 0) done.TrySetResult(answer.results);
            else done.TrySetException(new InvalidOperationException(what + ": the portal said no (code " + answer.response + ")"));
        });

        await call();
        Task finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        if (finished != done.Task) throw new TimeoutException(what + " got no answer from the portal");
        return await done.Task;
    }

    private void OnActivated((ObjectPath session, string id, ulong time, IDictionary<string, object> options) signal)
    {
        if (signal.session != _session) return;
        Action<string>? handler = Pressed;
        if (handler != null) handler(signal.id);
    }

    public async ValueTask DisposeAsync()
    {
        _activated?.Dispose();
        if (_connection != null)
        {
            _connection.Dispose();
            _connection = null;
        }
        await Task.CompletedTask;
    }
}

// Public because Tmds.DBus builds the proxy in an assembly of its own, which cannot implement an
// interface it is not allowed to see.
[DBusInterface("org.freedesktop.portal.GlobalShortcuts")]
public interface IGlobalShortcuts : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);

    Task<ObjectPath> BindShortcutsAsync(ObjectPath sessionHandle, (string, IDictionary<string, object>)[] shortcuts,
                                        string parentWindow, IDictionary<string, object> options);

    Task<IDisposable> WatchActivatedAsync(Action<(ObjectPath session, string id, ulong time, IDictionary<string, object> options)> handler);
}

[DBusInterface("org.freedesktop.portal.Request")]
public interface IRequest : IDBusObject
{
    Task<IDisposable> WatchResponseAsync(Action<(uint response, IDictionary<string, object> results)> handler);
}
