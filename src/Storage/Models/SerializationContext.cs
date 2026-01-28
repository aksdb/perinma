using System.Text.Json.Serialization;

namespace perinma.Storage.Models;

[JsonSerializable(typeof(GoogleCredentials))]
[JsonSerializable(typeof(CalDavCredentials))]
internal partial class CredentialsContext : JsonSerializerContext { }
