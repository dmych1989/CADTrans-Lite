// Services/MTextCodec.cs
// Handles stripping and restoring MText format codes using a placeholder mechanism.
// Supported codes: \P (paragraph), \L / \l (underline), \O / \o (overline),
//   \K / \k (strikethrough), {\\F<font>;...} (font group), \W<n>; (width factor),
//   \H<n>(x)?; (height multiplier), \S<a>/<b>; (stacking), \~ (non-breaking space),
//   \A<n>; (alignment), \C<n>; (color), \T<n>; (tracking), \Q<n>; (oblique angle).

using System.Text.RegularExpressions;

namespace CADTransLite.Core.Services;

/// <summary>
/// Converts MText raw strings to plain text (with placeholder tokens) and back.
/// </summary>
public static class MTextCodec
{
    // Placeholder sentinel characters (ASCII control chars, never appear in real text).
    private const string Prefix = "\x02MFMT\x02";
    private const string Suffix = "\x03";

    /// <summary>
    /// Public accessor for the placeholder prefix, used to detect whether
    /// a string contains binary placeholder tokens from <see cref="StripFormatCodes"/>.
    /// </summary>
    public static string PlaceholderPrefix => Prefix;

    // -----------------------------------------------------------------
    // Regex patterns for every recognised MText format code.
    // All patterns are pre-compiled for performance.
    // -----------------------------------------------------------------

    // {\\F<fontname>;<inner text>}  — font-change group
    // Captures the opening tag (group "open") and the inner text (group "inner").
    private static readonly Regex ReFontGroup = new(
        @"\{(?<open>\\F[^;]*;)(?<inner>[^}]*)\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // \S<a>/<b>;  or  \S<a>^<b>;  — stacking / fractions
    private static readonly Regex ReStack = new(
        @"\\S[^;]*;",
        RegexOptions.Compiled);

    // Parametric codes: \W, \H, \A, \T, \Q, \C, \p, \l, \L, \o, \O, \k, \K
    private static readonly Regex ReParamCode = new(
        @"\\[WHATQCSplLoOkK][^;]*;",
        RegexOptions.Compiled);

    // Simple toggle codes: \P, \~, \n (case-insensitive for \P)
    private static readonly Regex ReSimpleCode = new(
        @"\\[PpnN~]",
        RegexOptions.Compiled);

    // Bare grouping braces not consumed by ReFontGroup
    private static readonly Regex ReCurly = new(
        @"[{}]",
        RegexOptions.Compiled);

    // -----------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------

    /// <summary>
    /// Strips MText format codes from <paramref name="rawValue"/>, returning
    /// human-readable plain text.  Every stripped code sequence is replaced by
    /// a placeholder token stored in <paramref name="placeholders"/>, so that
    /// <see cref="RestoreFormatCodes"/> can reconstruct the formatting around
    /// the translated text.
    /// </summary>
    /// <param name="rawValue">Raw MText value string from the DXF entity.</param>
    /// <param name="placeholders">
    /// Output map: placeholder token → original code string.
    /// </param>
    /// <returns>Plain text, possibly containing embedded placeholder tokens.</returns>
    public static string StripFormatCodes(
        string rawValue,
        out Dictionary<string, string> placeholders)
    {
        // Use a local variable so lambdas can capture it (CS1628 workaround:
        // 'out' parameters cannot be used inside lambda expressions).
        var localPlaceholders = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrEmpty(rawValue))
        {
            placeholders = localPlaceholders;
            return rawValue;
        }

        int counter = 0;
        string current = rawValue;

        // ------------------------------------------------------------------
        // Step 1 — Font groups: {\\Ffontname;inner text}
        //   Replace the opening tag with a placeholder; preserve inner text;
        //   replace the closing "}" with another placeholder.
        // ------------------------------------------------------------------
        current = ReFontGroup.Replace(current, m =>
        {
            string openCode = m.Groups["open"].Value;  // e.g. {\F Arial;
            string innerText = m.Groups["inner"].Value;

            string openTok = NewToken(ref counter);
            string closeTok = NewToken(ref counter);
            localPlaceholders[openTok] = "{" + openCode;
            localPlaceholders[closeTok] = "}";

            return openTok + innerText + closeTok;
        });

        // ------------------------------------------------------------------
        // Step 2 — Stacking codes \S...;
        // ------------------------------------------------------------------
        current = ReStack.Replace(current, m =>
        {
            string tok = NewToken(ref counter);
            localPlaceholders[tok] = m.Value;
            return tok;
        });

        // ------------------------------------------------------------------
        // Step 3 — Parametric codes \W, \H, \A, \T, \Q, \C, \p, \l, \L, etc.
        // ------------------------------------------------------------------
        current = ReParamCode.Replace(current, m =>
        {
            string tok = NewToken(ref counter);
            localPlaceholders[tok] = m.Value;
            return tok;
        });

        // ------------------------------------------------------------------
        // Step 4 — Simple codes: \P, \~, \n → placeholder.
        // ------------------------------------------------------------------
        current = ReSimpleCode.Replace(current, m =>
        {
            string t = NewToken(ref counter);
            localPlaceholders[t] = m.Value;
            return t;
        });

        // ------------------------------------------------------------------
        // Step 5 — Bare curly braces remaining (grouping delimiters).
        // ------------------------------------------------------------------
        current = ReCurly.Replace(current, m =>
        {
            string tok = NewToken(ref counter);
            localPlaceholders[tok] = m.Value;
            return tok;
        });

        // ------------------------------------------------------------------
        // Step 6 — Unescape \\ → \ and \; → ; (must be last).
        // ------------------------------------------------------------------
        current = current.Replace(@"\\", "\x01BSLASH\x01");
        current = current.Replace(@"\;", ";");
        current = current.Replace("\x01BSLASH\x01", @"\");

        placeholders = localPlaceholders;
        return current;
    }

    /// <summary>
    /// Reconstructs a formatted MText value from translated plain text plus
    /// the placeholder map produced by <see cref="StripFormatCodes"/>.
    /// </summary>
    /// <param name="translatedText">
    /// Translated plain text.  May contain embedded placeholder tokens if the
    /// translator preserved them; plain newlines are re-encoded as <c>\P</c>.
    /// </param>
    /// <param name="placeholders">
    /// Placeholder map from the matching <see cref="StripFormatCodes"/> call.
    /// </param>
    /// <returns>Formatted MText string ready to write back to the DXF entity.</returns>
    public static string RestoreFormatCodes(
        string translatedText,
        Dictionary<string, string> placeholders)
    {
        ArgumentNullException.ThrowIfNull(placeholders);

        if (string.IsNullOrEmpty(translatedText))
            return string.Empty;

        if (placeholders.Count == 0)
        {
            return EscapeMText(translatedText).Replace("\n", @"\P");
        }

        // Split translated text at placeholder token boundaries so we can
        // escape MText special characters (\, {, }) only in the plain-text
        // segments, leaving the tokens untouched.
        // Token format: \x02MFMT\x02<n>\x03
        var tokenRegex = new Regex(
            Regex.Escape(Prefix) + @"\d+" + Regex.Escape(Suffix));

        var segments = new List<string>();
        int lastEnd = 0;

        foreach (Match m in tokenRegex.Matches(translatedText))
        {
            // Plain-text segment before this token — escape special chars.
            if (m.Index > lastEnd)
            {
                string plainSegment = translatedText[lastEnd..m.Index];
                segments.Add(EscapeMText(plainSegment));
            }

            // Placeholder token itself — keep as-is.
            segments.Add(m.Value);
            lastEnd = m.Index + m.Length;
        }

        // Trailing plain text after the last token.
        if (lastEnd < translatedText.Length)
        {
            segments.Add(EscapeMText(translatedText[lastEnd..]));
        }

        // If no tokens were found in the translated text, the entire text
        // is plain content.
        if (segments.Count == 0)
        {
            segments.Add(EscapeMText(translatedText));
        }

        string current = string.Concat(segments);

        // Re-encode plain newlines → \P (MText paragraph break).
        current = current.Replace("\n", @"\P");

        // Restore each placeholder token to its original code sequence.
        // Dictionary is insertion-ordered in .NET 5+, so no extra sort needed.
        foreach (var kvp in placeholders)
        {
            current = current.Replace(kvp.Key, kvp.Value);
        }

        return current;
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>Creates a unique placeholder token and bumps the counter.</summary>
    private static string NewToken(ref int counter) =>
        $"{Prefix}{counter++}{Suffix}";

    /// <summary>
    /// Escapes MText special characters in a plain-text string that does NOT
    /// already contain format codes.
    /// </summary>
    private static string EscapeMText(string plain) =>
        plain
            .Replace(@"\", @"\\")
            .Replace("{", @"\{")
            .Replace("}", @"\}");

    // -----------------------------------------------------------------
    // StripForTranslation — produces clean, human-readable text for
    // translation APIs. Format codes are removed; \P becomes newline.
    // -----------------------------------------------------------------

    /// <summary>
    /// Strips MText format codes from <paramref name="rawValue"/>, producing
    /// clean, human-readable text suitable for a translation API.
    /// Unlike <see cref="StripFormatCodes"/>, this method does NOT produce
    /// placeholder tokens; instead, <c>\P</c> paragraph breaks are converted
    /// to newlines and all other format codes are simply removed.
    /// </summary>
    /// <param name="rawValue">Raw MText value string from the DXF entity.</param>
    /// <returns>Clean text with format codes removed and <c>\P</c> → newline.</returns>
    public static string StripForTranslation(string rawValue)
    {
        if (string.IsNullOrEmpty(rawValue))
            return rawValue;

        string current = rawValue;

        // Step 1 — Font groups: {\\Ffontname;inner text} → keep inner text only
        current = ReFontGroup.Replace(current, m => m.Groups["inner"].Value);

        // Step 2 — Stacking codes \S...; → remove entirely
        current = ReStack.Replace(current, "");

        // Step 3 — Parametric codes \W, \H, \A, \T, \Q, \C, \p, \l, \L, etc. → remove
        current = ReParamCode.Replace(current, "");

        // Step 4 — \P → newline, \~ → non-breaking space, other simple codes → remove
        current = ReSimpleCode.Replace(current, m =>
        {
            char code = m.Value[1];
            if (code == 'P' || code == 'p')
                return "\n";
            if (code == '~')
                return "\u00A0"; // non-breaking space
            return "";
        });

        // Step 5 — Bare curly braces → remove
        current = current.Replace("{", "").Replace("}", "");

        // Step 6 — Unescape \\ → \ and \; → ; (must be last)
        current = current.Replace(@"\\", "\x01BSLASH\x01");
        current = current.Replace(@"\;", ";");
        current = current.Replace("\x01BSLASH\x01", @"\");

        return current.Trim();
    }
}
