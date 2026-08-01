using System.Net;
using System.Text.RegularExpressions;

namespace LegendsViewer.Backend.Analysis;

/// <summary>
/// The prose produced by <c>Print</c>/<c>ToLink</c> is written for the Vue frontend and embeds
/// Vuetify markup when <c>link</c> is true. Passing <c>link: false</c> already avoids the anchors;
/// this is the safety net that guarantees non-UI consumers never receive HTML, whatever an
/// individual event class decides to emit.
/// </summary>
public static partial class PlainText
{
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string Strip(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string text = HtmlTag().Replace(value, string.Empty);
        text = WebUtility.HtmlDecode(text);
        return Whitespace().Replace(text, " ").Trim();
    }
}
