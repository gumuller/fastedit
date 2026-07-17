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

    private RegexCache _regexCache = new();

    public bool Matches(string lineText)
    {
        if (string.IsNullOrEmpty(Pattern)) return false;

        try
        {
            if (IsRegex)
            {
                return _regexCache.GetOrCompile(Pattern, IsCaseSensitive)?.IsMatch(lineText) ?? false;
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

    internal LineFilter CreateSnapshot()
    {
        var regexCache = _regexCache;

        return new LineFilter
        {
            Pattern = Pattern,
            IsRegex = IsRegex,
            IsCaseSensitive = IsCaseSensitive,
            IsEnabled = IsEnabled,
            IsExcluding = IsExcluding,
            BackgroundColor = BackgroundColor,
            _regexCache = regexCache
        };
    }

    private sealed class RegexCache
    {
        private readonly object _sync = new();
        private string? _pattern;
        private bool _isCaseSensitive;
        private bool _initialized;
        private Regex? _compiledRegex;

        public Regex? GetOrCompile(string pattern, bool isCaseSensitive)
        {
            lock (_sync)
            {
                if (_initialized &&
                    _pattern == pattern &&
                    _isCaseSensitive == isCaseSensitive)
                {
                    return _compiledRegex;
                }

                var options = RegexOptions.Compiled;
                if (!isCaseSensitive)
                    options |= RegexOptions.IgnoreCase;

                try
                {
                    _compiledRegex = new Regex(pattern, options, TimeSpan.FromMilliseconds(200));
                }
                catch
                {
                    _compiledRegex = null;
                }

                _pattern = pattern;
                _isCaseSensitive = isCaseSensitive;
                _initialized = true;
                return _compiledRegex;
            }
        }
    }
}

public record LineFilterResult(bool MatchesInclude, bool MatchesExclude, LineFilter? MatchingFilter)
{
    public static readonly LineFilterResult NoMatch = new(false, false, null);
    public bool IsVisible => MatchesInclude && !MatchesExclude;
}
