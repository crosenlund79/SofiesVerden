using System.Security.Cryptography;

namespace SofiesUnivers.Core;

public static class Hashing
{
    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Today's date in the user's local time zone.</summary>
    DateOnly Today { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
