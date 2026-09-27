using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Poe2StashPricer.Storage;

/// <summary>
/// The one place the JSON settings live. Upstream used JavaScriptSerializer, which matched property
/// names loosely; System.Text.Json is told to do the same so files written by either name style load.
/// Files are read and written as streams: a tab profile or a scan result runs to hundreds of kilobytes,
/// and nothing here ever wants it as a string as well.
/// </summary>
internal static class Json
{
    /// <summary>The compile-time description of <typeparamref name="T"/>; see <see cref="JsonContext"/>.</summary>
    private static JsonTypeInfo<T> TypeInfo<T>()
    {
        JsonTypeInfo info = JsonContext.Default.GetTypeInfo(typeof(T));
        if (info == null)
            throw new InvalidOperationException(typeof(T).Name + " is not listed in " + nameof(JsonContext)
                                                + ", so it cannot be read or written");
        return (JsonTypeInfo<T>)info;
    }

    public static T Read<T>(Stream json) { return JsonSerializer.Deserialize(json, TypeInfo<T>()); }

    public static T Load<T>(string path)
    {
        using (FileStream f = File.OpenRead(path))
            return Read<T>(f);
    }

    public static void Save<T>(string path, T value)
    {
        using (FileStream f = File.Create(path))
            JsonSerializer.Serialize(f, value, TypeInfo<T>());
    }
}
