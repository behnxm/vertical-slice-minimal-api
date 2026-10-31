using System.Buffers.Text;
using System.Text;
using TaskTracker.Api.Common.Results;

namespace TaskTracker.Api.Common.Pagination;

/// <summary>
/// Keyset cursor encoding and strictly validated decoding for stable, performant pagination.
/// Formatted as URL-safe Base64: "{sortKey}|{id}".
/// </summary>
public static class Cursor
{
    private const char Separator = '|';

    /// <summary>
    /// Encodes a DateTimeOffset sort key and Guid identifier into an opaque, URL-safe cursor string.
    /// </summary>
    public static string Encode(DateTimeOffset sortKey, Guid id)
    {
        var raw = $"{sortKey.ToUniversalTime():O}{Separator}{id:N}";
        return Base64UrlEncode(Encoding.UTF8.GetBytes(raw));
    }

    /// <summary>
    /// Decodes and strictly validates an opaque cursor string into a DateTimeOffset sort key and Guid identifier.
    /// Returns a validation error if the cursor is malformed, truncated, or tampered with.
    /// </summary>
    public static Result<(DateTimeOffset SortKey, Guid Id)> TryDecode(string? cursorString)
    {
        if (string.IsNullOrWhiteSpace(cursorString))
        {
            return Error.Validation("pagination.cursor_empty", "The provided pagination cursor is empty.");
        }

        try
        {
            var bytes = Base64UrlDecode(cursorString);
            var decoded = Encoding.UTF8.GetString(bytes);
            var parts = decoded.Split(Separator);

            if (parts.Length != 2)
            {
                return Error.Validation("pagination.cursor_invalid", "The pagination cursor format is invalid.");
            }

            if (!DateTimeOffset.TryParse(parts[0], out var sortKey))
            {
                return Error.Validation("pagination.cursor_invalid", "The pagination cursor timestamp is invalid.");
            }

            if (!Guid.TryParseExact(parts[1], "N", out var id))
            {
                return Error.Validation("pagination.cursor_invalid", "The pagination cursor identifier is invalid.");
            }

            return (sortKey, id);
        }
        catch (Exception)
        {
            return Error.Validation("pagination.cursor_invalid", "The pagination cursor is corrupted or tampered with.");
        }
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var incoming = input.Replace('-', '+').Replace('_', '/');
        switch (incoming.Length % 4)
        {
            case 2: incoming += "=="; break;
            case 3: incoming += "="; break;
        }

        return Convert.FromBase64String(incoming);
    }
}
