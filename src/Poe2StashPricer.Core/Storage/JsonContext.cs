using System.Collections.Generic;
using System.Text.Json.Serialization;
using Poe2StashPricer.Detection;
using Poe2StashPricer.Tabs;

namespace Poe2StashPricer.Storage;

/// <summary>
/// Every shape the app reads or writes as JSON, listed so the serialiser is built at compile time.
///
/// Reflection would do this at run time, and does when nothing is trimmed - but a trimmed build has no way
/// to know which properties are only ever reached that way, drops them, and then quietly loads nothing at
/// all. Saying the types here keeps a small build honest.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(TabProfile))]
[JsonSerializable(typeof(List<TabProfile>))]
[JsonSerializable(typeof(TabResult))]
[JsonSerializable(typeof(Dictionary<string, TabResult>))]
[JsonSerializable(typeof(DigitReader.Store))]
internal partial class JsonContext : JsonSerializerContext
{
}
