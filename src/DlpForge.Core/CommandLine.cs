using System.Text;

namespace DlpForge.Core;

/// <summary>
/// Splits a typed argument string into arguments the way a user expects: spaces separate arguments,
/// "double" or 'single' quotes group them, and a backslash escapes only a quote, so Windows paths
/// like C:\Videos stay as they are. Nothing goes through a shell.
/// </summary>
public static class CommandLine
{
    public static List<string> Split(string? text)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return args;

        var current = new StringBuilder();
        bool inArg = false;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && (text[i + 1] == '"' || text[i + 1] == '\''))
            {
                current.Append(text[++i]);
                inArg = true;
            }
            else if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else current.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                inArg = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inArg) args.Add(current.ToString());
                current.Clear();
                inArg = false;
            }
            else
            {
                current.Append(c);
                inArg = true;
            }
        }
        if (inArg) args.Add(current.ToString());
        return args;
    }

    /// <summary>Joins arguments for display, quoting the ones that need it.</summary>
    public static string Join(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));

    public static string Quote(string a) =>
        a.Length == 0 ? "\"\""
        : a.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'') ? "\"" + a.Replace("\"", "\\\"") + "\""
        : a;
}
