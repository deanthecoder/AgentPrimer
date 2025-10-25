// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any non-commercial
// purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Text;

namespace AgentPrimer.Utilities;

/// <summary>
/// Centralizes console formatting helpers for headings and wrapped lists.
/// </summary>
internal static class ConsoleSectionWriter
{
    public static void PrintWrappedList(StringBuilder sb, IEnumerable<string> items, string indent, int maxWidth, string separator = " | ")
    {
        if (items is null)
        {
            sb.AppendLine(indent + "none");
            return;
        }

        var line = indent;
        var firstInLine = true;

        foreach (var item in items)
        {
            var token = firstInLine ? item : separator + item;

            if (line.Length + token.Length > maxWidth)
            {
                sb.AppendLine(line);
                line = indent + item;
                firstInLine = false;
                continue;
            }

            line += token;
            firstInLine = false;
        }

        if (!string.IsNullOrWhiteSpace(line.Trim()))
            sb.AppendLine(line);
    }
}
