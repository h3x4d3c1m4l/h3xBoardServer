using System.Buffers;
using System.Security.Cryptography;

namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// Share-code generation and normalization. Codes are 6 characters from an ambiguity-free alphabet
/// (no 0/O, 1/I/L) so they can be read out loud or typed from a projector screen. Generation uses a
/// cryptographic RNG — codes are the only thing protecting a session from uninvited viewers.
/// </summary>
public static class ShareCodes
{
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 6;

    private static readonly SearchValues<char> AlphabetSearch = SearchValues.Create(Alphabet);

    public static string Generate()
        => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>
    /// Canonicalizes client input: strips whitespace and dashes (users may type "ABC-123" or
    /// "abc 123") and uppercases. Does not validate — see <see cref="IsValid"/>.
    /// </summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return "";

        Span<char> buffer = stackalloc char[code.Length];
        var length = 0;
        foreach (var c in code)
        {
            if (char.IsWhiteSpace(c) || c == '-')
                continue;
            buffer[length++] = char.ToUpperInvariant(c);
        }

        return new string(buffer[..length]);
    }

    /// <summary>True when <paramref name="code"/> is a normalized, well-formed share code.</summary>
    public static bool IsValid(string code)
        => code.Length == Length && !code.AsSpan().ContainsAnyExcept(AlphabetSearch);
}
