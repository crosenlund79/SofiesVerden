using System.Text.Json.Serialization;
using SofiesUnivers.Core.Backup;
using SofiesUnivers.Core.Model;

namespace SofiesUnivers.Core.Storage;

/// <summary>Source-generated JSON metadata: trimming-safe and fast in WebAssembly.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(AppData))]
[JsonSerializable(typeof(Session))]
[JsonSerializable(typeof(Payment))]
[JsonSerializable(typeof(LookupItem))]
[JsonSerializable(typeof(Expense))]
[JsonSerializable(typeof(Trip))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(BackupStatus))]
[JsonSerializable(typeof(StorageMeta))]
[JsonSerializable(typeof(BackupInfo))]
[JsonSerializable(typeof(List<Session>))]
[JsonSerializable(typeof(List<Payment>))]
[JsonSerializable(typeof(List<LookupItem>))]
[JsonSerializable(typeof(List<Expense>))]
[JsonSerializable(typeof(List<Trip>))]
public sealed partial class AppJsonContext : JsonSerializerContext;
