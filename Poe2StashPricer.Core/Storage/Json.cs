using System.IO;
using System.Text.Json;

namespace Poe2StashPricer.Storage;

/// <summary>
/// The one place the JSON settings live. Upstream used JavaScriptSerializer, which matched property
/// names loosely; System.Text.Json is told to do the same so files written by either name style load.
/// Files are read and written as streams: a tab profile or a scan result runs to hundreds of kilobytes,
/// and nothing here ever wants it as a string as well.
/// </summary>
internal static class Json
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    public static T Read<T>(Stream json) { return JsonSerializer.Deserialize<T>(json, Options); }

    public static T Load<T>(string path)
    {
        using (FileStream f = File.OpenRead(path))
            return Read<T>(f);
    }

    public static void Save<T>(string path, T value)
    {
        using (FileStream f = File.Create(path))
            JsonSerializer.Serialize(f, value, Options);
    }
}
