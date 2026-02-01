using System.Text.Json.Serialization;

namespace perinma.Storage.Models;

[JsonSerializable(typeof(GoogleCredentials))]
[JsonSerializable(typeof(CalDavCredentials))]
[JsonSerializable(typeof(CardDavCredentials))]
internal partial class CredentialsContext : JsonSerializerContext { }
