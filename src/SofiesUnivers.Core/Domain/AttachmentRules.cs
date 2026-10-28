using System.Text;

namespace SofiesUnivers.Core.Domain;

/// <summary>Validation of receipt files. The file type is detected from content, not trusted from the browser.</summary>
public static class AttachmentRules
{
    /// <summary>Maximum stored size of one receipt (after optional image compression in the browser).</summary>
    public const long MaxStoredBytes = 15 * 1024 * 1024;

    /// <summary>Maximum size of a file Sofie may pick before compression.</summary>
    public const long MaxPickedBytes = 40 * 1024 * 1024;

    public sealed record DetectedType(string ContentType, string Extension, bool IsImage);

    public static DetectedType? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return new("image/jpeg", ".jpg", true);
        }

        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return new("image/png", ".png", true);
        }

        if (data.Length >= 4 && data[..4].SequenceEqual("%PDF"u8))
        {
            return new("application/pdf", ".pdf", false);
        }

        if (data.Length >= 6 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
        {
            return new("image/gif", ".gif", true);
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return new("image/webp", ".webp", true);
        }

        if (data.Length >= 12 && data[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = Encoding.ASCII.GetString(data[8..12]);
            if (brand is "heic" or "heix" or "hevc" or "hevx" or "heim" or "heis" or "mif1" or "msf1" or "heif")
            {
                return new("image/heic", ".heic", true);
            }
        }

        return null;
    }

    /// <summary>Returns a Danish error message, or null when the file is acceptable.</summary>
    public static string? Check(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return "Filen er tom.";
        }

        if (data.Length > MaxStoredBytes)
        {
            return $"Filen er for stor (højst {MaxStoredBytes / 1024 / 1024} MB).";
        }

        return Detect(data) is null
            ? "Filtypen understøttes ikke. Vælg et billede (JPG, PNG, HEIC) eller en PDF."
            : null;
    }

    /// <summary>A safe file name with the extension matching the detected content.</summary>
    public static string SafeFileName(string? original, DetectedType type)
    {
        var name = Path.GetFileNameWithoutExtension(original ?? string.Empty);
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' or '.')
            {
                sb.Append(c);
            }
        }

        var cleaned = sb.ToString().Trim().Trim('.');
        if (cleaned.Length == 0)
        {
            cleaned = "kvittering";
        }

        if (cleaned.Length > 80)
        {
            cleaned = cleaned[..80];
        }

        return cleaned + type.Extension;
    }
}
