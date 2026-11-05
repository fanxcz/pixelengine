using System.Text.RegularExpressions;

namespace PixelEngine.Editor;

/// <summary>
/// Parses MSBuild/Roslyn diagnostic lines from terminal or build output. Extracted from the
/// shell so the parsing rules are unit-testable without a window. Both the classic
/// <c>file(line,col): error CSnnnn: text</c> shape and the SDK-style
/// <c>file:line:col: error CSnnnn: text</c> shape are accepted; MSBuild summaries, project
/// banners and status lines are rejected so they never pollute the Problems list.
/// </summary>
internal static class BuildDiagnosticLineParser
{
    private static readonly Regex DiagnosticPattern = new(
        @"^(?<file>.+?)(?:\((?<line>\d+),(?<column>\d+)\)|:(?<line2>\d+):(?<column2>\d+)):\s*(?<severity>error|warning)\s+(?<code>[A-Za-z]+[0-9]+):\s*(?<message>.*?)(?:\s+\[[^\[\]]*\])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Attempts to parse one output line into its diagnostic components.</summary>
    public static bool TryParse(string? line, out string file, out int lineNumber, out int column,
        out string severity, out string code, out string message)
    {
        file = string.Empty;
        lineNumber = 0;
        column = 0;
        severity = string.Empty;
        code = string.Empty;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(line)) return false;
        var trimmed = line.Trim();
        // A diagnostic always carries a file path followed by a location, so reject anything
        // that starts with a project banner or has no code segment.
        if (trimmed.Length > 4096) return false;
        var match = DiagnosticPattern.Match(trimmed);
        if (!match.Success) return false;

        var fileGroup = match.Groups["file"];
        if (!fileGroup.Success || fileGroup.Value.Length == 0) return false;
        var lineGroup = match.Groups["line"].Success ? match.Groups["line"] : match.Groups["line2"];
        var columnGroup = match.Groups["column"].Success ? match.Groups["column"] : match.Groups["column2"];
        if (!int.TryParse(lineGroup.Value, out lineNumber) || lineNumber <= 0) return false;
        if (!int.TryParse(columnGroup.Value, out column) || column < 0) return false;

        file = fileGroup.Value.Trim().Trim('"');
        severity = match.Groups["severity"].Value.ToLowerInvariant();
        code = match.Groups["code"].Value.ToUpperInvariant();
        message = match.Groups["message"].Value.Trim();
        return file.Length > 0 && code.Length > 0;
    }

    /// <summary>Parses into a <see cref="BuildProblem"/> when the line is a compiler diagnostic.</summary>
    public static bool TryParse(string? line, out BuildProblem problem)
    {
        problem = default!;
        if (!TryParse(line, out var file, out var lineNumber, out var column, out var severity, out var code, out var message))
            return false;
        problem = new BuildProblem(file, lineNumber, column, severity, code, message);
        return true;
    }
}
