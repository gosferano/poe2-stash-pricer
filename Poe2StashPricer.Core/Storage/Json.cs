using System.Text.Json;

namespace Poe2StashPricer.Storage
{
    /// <summary>
    /// The one place the JSON settings live. Upstream used JavaScriptSerializer, which matched property
    /// names loosely; System.Text.Json is told to do the same so files written by either name style load.
    /// </summary>
    static class Json
    {
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public static T Deserialize<T>(string text) { return JsonSerializer.Deserialize<T>(text, Options); }

        public static string Serialize<T>(T value) { return JsonSerializer.Serialize(value, Options); }
    }
}
