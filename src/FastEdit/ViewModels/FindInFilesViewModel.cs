using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastEdit.Services.Interfaces;

namespace FastEdit.ViewModels;

public partial class FindInFilesViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Maximum results retained in the UI collection for one search.
    /// </summary>
    public const int ResultLimit = 5_000;
    private const int UiBatchSize = 50;

    private readonly IFileSystemService _fileSystemService;
    private readonly IDispatcherService _dispatcherService;
    private CancellationTokenSource? _searchCts;
    private int _searchVersion;
    private bool _disposed;

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

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        if (!CanSearch())
            return;

        var request = CreateSearchRequest();
        var version = Interlocked.Increment(ref _searchVersion);
        var searchCts = new CancellationTokenSource();
        var token = searchCts.Token;
        var previous = Interlocked.Exchange(ref _searchCts, searchCts);
        previous?.Cancel();
        BeginSearch();

        try
        {
            var summary = await Task.Run(
                () => RunSearch(request, version, searchCts, token),
                token);

            if (!IsCurrentSearch(version, searchCts))
                return;

            StatusText = summary.LimitReached
                ? $"Found {summary.MatchCount:N0} match(es) in {summary.FileCount:N0} file(s) (result limit reached)"
                : $"Found {summary.MatchCount:N0} match(es) in {summary.FileCount:N0} file(s)";
        }
        catch (OperationCanceledException) when (searchCts.IsCancellationRequested)
        {
            if (IsCurrentSearch(version, searchCts))
                StatusText = "Search cancelled";
        }
        catch (Exception ex)
        {
            if (IsCurrentSearch(version, searchCts))
                StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _searchCts, null, searchCts),
                    searchCts))
            {
                IsSearching = false;
            }

            searchCts.Dispose();
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

    private SearchSummary RunSearch(
        SearchRequest request,
        int version,
        CancellationTokenSource searchCts,
        CancellationToken token)
    {
        var matcher = CreateLineMatcher(request);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var batch = new List<SearchResult>(UiBatchSize);
        var matchCount = 0;
        var fileCount = 0;

        foreach (var file in EnumerateCandidateFiles(request))
        {
            token.ThrowIfCancellationRequested();
            if (!seenFiles.Add(file))
                continue;

            fileCount++;
            if (ShouldSkipFile(file))
                continue;

            if (AddFileMatches(
                    file,
                    request,
                    matcher,
                    version,
                    searchCts,
                    token,
                    batch,
                    ref matchCount))
            {
                FlushBatch(batch, version, searchCts);
                return new SearchSummary(matchCount, fileCount, true);
            }
        }

        FlushBatch(batch, version, searchCts);
        return new SearchSummary(matchCount, fileCount, false);
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
            .SelectMany(filter => EnumerateFilesSafely(request.FolderPath, filter));

    private static IEnumerable<string> SplitFileFilters(string fileFilter)
    {
        var filters = fileFilter.Split(';', '|', ',')
            .Select(f => f.Trim())
            .Where(f => !string.IsNullOrEmpty(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return filters.Length == 0 ? ["*.*"] : filters;
    }

    private IEnumerable<string> EnumerateFilesSafely(string folderPath, string filter)
    {
        IEnumerable<string> files;
        try
        {
            files = _fileSystemService.EnumerateFiles(folderPath, filter, recursive: true);
        }
        catch (Exception ex) when (IsSkippableFileAccessException(ex))
        {
            return [];
        }

        return EnumerateSafely(files);
    }

    private static IEnumerable<string> EnumerateSafely(IEnumerable<string> files)
    {
        if (!TryGetEnumerator(files, out var enumerator))
            yield break;

        using (enumerator)
        {
            while (TryMoveNext(enumerator, out var file))
                yield return file;
        }
    }

    private static bool TryGetEnumerator(
        IEnumerable<string> files,
        out IEnumerator<string> enumerator)
    {
        try
        {
            enumerator = files.GetEnumerator();
            return true;
        }
        catch (Exception ex) when (IsSkippableFileAccessException(ex))
        {
            enumerator = null!;
            return false;
        }
    }

    private static bool TryMoveNext(
        IEnumerator<string> enumerator,
        out string file)
    {
        try
        {
            if (enumerator.MoveNext())
            {
                file = enumerator.Current;
                return true;
            }
        }
        catch (Exception ex) when (IsSkippableFileAccessException(ex))
        {
        }

        file = string.Empty;
        return false;
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

    private bool AddFileMatches(
        string file,
        SearchRequest request,
        Func<string, bool> matcher,
        int version,
        CancellationTokenSource searchCts,
        CancellationToken token,
        List<SearchResult> batch,
        ref int matchCount)
    {
        try
        {
            var lineNumber = 0;
            foreach (var line in _fileSystemService.ReadLines(file))
            {
                token.ThrowIfCancellationRequested();
                lineNumber++;
                if (!matcher(line))
                    continue;

                batch.Add(CreateSearchResult(file, request, line, lineNumber));
                matchCount++;
                if (batch.Count >= UiBatchSize)
                    FlushBatch(batch, version, searchCts);

                if (matchCount >= ResultLimit)
                    return true;
            }

            return false;
        }
        catch (Exception ex) when (IsSkippableFileAccessException(ex))
        {
            return false;
        }
    }

    private static SearchResult CreateSearchResult(
        string file,
        SearchRequest request,
        string line,
        int lineNumber)
    {
        return new SearchResult
        {
            FilePath = file,
            RelativePath = Path.GetRelativePath(request.FolderPath, file),
            LineNumber = lineNumber,
            LineText = line.TrimStart(),
            FileName = Path.GetFileName(file)
        };
    }

    private void FlushBatch(
        List<SearchResult> batch,
        int version,
        CancellationTokenSource searchCts)
    {
        if (batch.Count == 0)
            return;

        var pending = batch.ToArray();
        batch.Clear();
        _dispatcherService.Invoke(() =>
        {
            if (!IsCurrentSearch(version, searchCts))
                return;

            foreach (var result in pending)
                Results.Add(result);
        });
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
        CancelAndInvalidateSearch();
        Results.Clear();
        StatusText = "Ready";
        IsSearching = false;
    }

    [RelayCommand]
    private void CancelSearch()
    {
        Volatile.Read(ref _searchCts)?.Cancel();
    }

    private bool IsCurrentSearch(
        int version,
        CancellationTokenSource searchCts) =>
        version == Volatile.Read(ref _searchVersion) &&
        ReferenceEquals(Volatile.Read(ref _searchCts), searchCts);

    private void CancelAndInvalidateSearch()
    {
        Interlocked.Increment(ref _searchVersion);
        var searchCts = Interlocked.Exchange(ref _searchCts, null);
        searchCts?.Cancel();
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelAndInvalidateSearch();
        GC.SuppressFinalize(this);
    }

    private readonly record struct SearchSummary(
        int MatchCount,
        int FileCount,
        bool LimitReached);
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
