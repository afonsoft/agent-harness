using System.Text;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// SPEC-20261023 RF-B01: shared, idempotent sanitizer for external text that
/// reaches the model wire (fetch/browser output, mailbox payloads, GitHub
/// issue bodies and comments). Strips zero-width and bidirectional control
/// characters and flags homoglyph runs — known ASCII lookalike letters
/// (Cyrillic/Greek) sitting next to ASCII alphanumerics, the classic phishing
/// pattern — by replacing them with U+FFFD.
/// </summary>
public static class UntrustedContentSanitizer
{
    // ASCII lookalikes most abused in homoglyph attacks: lowercase Cyrillic
    // (а с е і ј о р ѕ х у һ), uppercase Cyrillic (А В Е І Ј К М Н О Р Ѕ Т Х)
    // and Greek omicron/nu. Latin-1 accents (é ç ã …) are NOT confusables.
    private static readonly HashSet<char> AsciiLookalikes =
    [
        'а', 'с', 'е', 'і', 'ј', 'о', 'р', 'ѕ', 'х', 'у', 'һ',
        'А', 'В', 'Е', 'І', 'Ј', 'К', 'М', 'Н', 'О', 'Р', 'Ѕ', 'Т', 'Х',
        'ο', 'ν',
    ];

    /// <summary>True for zero-width and bidirectional format/override characters.</summary>
    private static bool IsInvisible(char ch) =>
        ch is >= '\u200B' and <= '\u200F'
            or >= '\u2060' and <= '\u2064'
            or >= '\u202A' and <= '\u202E'
            or >= '\u2066' and <= '\u2069'
            or '\uFEFF';

    private static bool IsAsciiAlnum(string text, int index) =>
        index >= 0 && index < text.Length
            && text[index] is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    /// <summary>
    /// Removes invisible/bidi characters and replaces ASCII-lookalike letters
    /// adjacent to ASCII alphanumerics with U+FFFD. Idempotent: sanitizing the
    /// output yields the same string.
    /// </summary>
    public static string? Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var drop = IsInvisible(ch);
            var flag = !drop && AsciiLookalikes.Contains(ch)
                && (IsAsciiAlnum(text, i - 1) || IsAsciiAlnum(text, i + 1));
            if (drop || flag)
            {
                builder ??= new StringBuilder(text.Length).Append(text, 0, i);
                if (flag)
                {
                    builder.Append('�');
                }
            }
            else
            {
                builder?.Append(ch);
            }
        }

        return builder?.ToString() ?? text;
    }
}
