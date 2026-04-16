using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FastEdit.Models;

public partial class LineFilter : ObservableObject
{
    [ObservableProperty] private string _pattern = "";
    [ObservableProperty] private bool _isRegex;
    [ObservableProperty] private bool _isCaseSensitive;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _isExcluding;
    [ObservableProperty] private string _backgroundColor = "#4488FF";

    private Regex? _compiledRegex;
    private string? _lastCompiledPattern;
    private bool _lastIsRegex;
    private bool _lastIsCaseSensitive;

    public bool Matches(string lineText)
    {
        if (string.IsNullOrEmpty(Pattern)) return false;

        try
        {
            if (IsRegex)
            {
                EnsureRegexCompiled();
                return _compiledRegex?.IsMatch(lineText) ?? false;
            }

            var comparison = IsCaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            return lineText.Contains(Pattern, comparison);
        }
        catch
        {
            return false;
        }
    }

    private void EnsureRegexCompiled()
    {
        if (_compiledRegex != null &&
            _lastCompiledPattern == Pattern &&
            _lastIsRegex == IsRegex &&
            _lastIsCaseSensitive == IsCaseSensitive)
            return;

        var options = RegexOptions.Compiled;
        if (!IsCaseSensitive) options |= RegexOptions.IgnoreCase;

        try
        {
            _compiledRegex = new Regex(Pattern, options, TimeSpan.FromMilliseconds(200));
            _lastCompiledPattern = Pattern;
            _lastIsRegex = IsRegex;
            _lastIsCaseSensitive = IsCaseSensitive;
        }
        catch
        {
            _compiledRegex = null;
        }
    }
}

public record LineFilterResult(bool MatchesInclude, bool MatchesExclude, LineFilter? MatchingFilter)
{
    public static readonly LineFilterResult NoMatch = new(false, false, null);
    public bool IsVisible => MatchesInclude && !MatchesExclude;
}
