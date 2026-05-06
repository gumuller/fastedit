using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastEdit.Services.Interfaces;

namespace FastEdit.ViewModels;

public partial class FindInFilesViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IDispatcherService _dispatcherService;

    public FindInFilesViewModel(IFileSystemService fileSystemService, IDispatcherService dispatcherService)
    {
        _fileSystemService = fileSystemService;
        _dispatcherService = dispatcherService;
    }

    [ObservableProperty]
    private string _searchPattern = string.Empty;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private string _fileFilter = "*.*";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private ObservableCollection<SearchResult> _results = new();

    public event EventHandler<(string filePath, int line)>? NavigateToResult;

    public string? FolderPath { get; set; }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (!CanSearch())
            return;

        var request = CreateSearchRequest();
        BeginSearch();

        try
        {
            var summary = await Task.Run(() => RunSearch(request));
            StatusText = $"Found {summary.MatchCount} match(es) in {summary.FileCount} file(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanSearch() =>
        !string.IsNullOrEmpty(SearchPattern) &&
        !string.IsNullOrEmpty(FolderPath) &&
        _fileSystemService.DirectoryExists(FolderPath);

    private SearchRequest CreateSearchRequest() => new(
        FolderPath!,
        SearchPattern,
        MatchCase,
        UseRegex,
        FileFilter);

    private void BeginSearch()
    {
        IsSearching = true;
        Results.Clear();
        StatusText = "Searching...";
    }

    private SearchSummary RunSearch(SearchRequest request)
    {
        var matcher = CreateLineMatcher(request);
        var files = EnumerateCandidateFiles(request).ToList();
        var matchCount = 0;

        foreach (var file in files)
        {
            if (matchCount > 5000) break; // safety limit
            if (ShouldSkipFile(file)) continue;

            matchCount += AddFileMatches(file, request, matcher);
        }

        return new SearchSummary(matchCount, files.Count);
    }

    private Func<string, bool> CreateLineMatcher(SearchRequest request)
    {
        if (request.UseRegex)
        {
            var options = request.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            var regex = new Regex(request.SearchPattern, options | RegexOptions.Compiled, TimeSpan.FromSeconds(5));
            return regex.IsMatch;
        }

        var comparison = request.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return line => line.Contains(request.SearchPattern, comparison);
    }

    private IEnumerable<string> EnumerateCandidateFiles(SearchRequest request) =>
        SplitFileFilters(request.FileFilter)
            .SelectMany(filter => EnumerateFilesSafely(request.FolderPath, filter))
            .Distinct();

    private static IEnumerable<string> SplitFileFilters(string fileFilter)
    {
        var filters = fileFilter.Split(';', '|', ',')
            .Select(f => f.Trim())
            .Where(f => !string.IsNullOrEmpty(f))
            .ToList();

        return filters.Count == 0 ? ["*.*"] : filters;
    }

    private IEnumerable<string> EnumerateFilesSafely(string folderPath, string filter)
    {
        try
        {
            return _fileSystemService.EnumerateFiles(folderPath, filter, recursive: true).ToList();
        }
        catch (Exception ex) when (IsSkippableFileAccessException(ex))
        {
            return [];
        }
    }

    private bool ShouldSkipFile(string file)
    {
        try
        {
            using var checkStream = _fileSystemService.OpenRead(file);
            var buffer = new byte[Math.Min(8192, checkStream.Length)];
            var bytesRead = checkStream.Read(buffer, 0, buffer.Length);
            return IsBinary(buffer, bytesRead);
        }
        catch (Exception ex) when (IsSkippableFileAccessException(ex))
        {
            return true;
        }
    }

    private int AddFileMatches(string file, SearchRequest request, Func<string, bool> matcher)
    {
        try
        {
            var lines = _fileSystemService.ReadLines(file).ToList();
            var matches = CreateSearchResults(file, request, lines, matcher);

            foreach (var result in matches)
                _dispatcherService.Invoke(() => Results.Add(result));

            return matches.Count;
        }
        catch (Exception ex) when (IsSkippableFileAccessException(ex))
        {
            return 0;
        }
    }

    private static List<SearchResult> CreateSearchResults(
        string file,
        SearchRequest request,
        IReadOnlyList<string> lines,
        Func<string, bool> matcher)
    {
        var matches = new List<SearchResult>();

        for (var i = 0; i < lines.Count; i++)
        {
            if (!matcher(lines[i]))
                continue;

            matches.Add(new SearchResult
            {
                FilePath = file,
                RelativePath = Path.GetRelativePath(request.FolderPath, file),
                LineNumber = i + 1,
                LineText = lines[i].TrimStart(),
                FileName = Path.GetFileName(file)
            });
        }

        return matches;
    }

    private static bool IsSkippableFileAccessException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException;

    [RelayCommand]
    private void NavigateTo(SearchResult? result)
    {
        if (result == null) return;
        NavigateToResult?.Invoke(this, (result.FilePath, result.LineNumber));
    }

    [RelayCommand]
    private void ClearResults()
    {
        Results.Clear();
        StatusText = "Ready";
    }

    private static bool IsBinary(byte[] buffer, int length)
    {
        for (int i = 0; i < length; i++)
        {
            byte b = buffer[i];
            if (b == 0) return true;
            if (b < 8 && b != 0) return true;
        }
        return false;
    }

    private sealed record SearchRequest(
        string FolderPath,
        string SearchPattern,
        bool MatchCase,
        bool UseRegex,
        string FileFilter);

    private readonly record struct SearchSummary(int MatchCount, int FileCount);
}

public class SearchResult
{
    public string FilePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string LineText { get; set; } = string.Empty;

    public string Display => $"{RelativePath}:{LineNumber}  {LineText}";
}
