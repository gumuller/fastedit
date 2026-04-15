using System.Collections.ObjectModel;
using System.IO;
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
        if (string.IsNullOrEmpty(SearchPattern) || string.IsNullOrEmpty(FolderPath))
            return;

        if (!_fileSystemService.DirectoryExists(FolderPath)) return;

        IsSearching = true;
        Results.Clear();
        StatusText = "Searching...";

        try
        {
            await Task.Run(() =>
            {
                var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                Regex? regex = null;
                if (UseRegex)
                {
                    var options = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                    regex = new Regex(SearchPattern, options | RegexOptions.Compiled, TimeSpan.FromSeconds(5));
                }

                var filters = FileFilter.Split(';', '|', ',')
                    .Select(f => f.Trim())
                    .Where(f => !string.IsNullOrEmpty(f))
                    .ToList();

                if (filters.Count == 0) filters.Add("*.*");

                var files = new List<string>();
                foreach (var filter in filters)
                {
                    try
                    {
                        files.AddRange(_fileSystemService.EnumerateFiles(FolderPath, filter, recursive: true));
                    }
                    catch { /* skip inaccessible */ }
                }

                files = files.Distinct().ToList();
                int matchCount = 0;

                foreach (var file in files)
                {
                    if (matchCount > 5000) break; // safety limit

                    try
                    {
                        // Skip binary files (check first 8KB)
                        using var checkStream = _fileSystemService.OpenRead(file);
                        var buffer = new byte[Math.Min(8192, checkStream.Length)];
                        int bytesRead = checkStream.Read(buffer, 0, buffer.Length);
                        if (IsBinary(buffer, bytesRead)) continue;
                    }
                    catch { continue; }

                    try
                    {
                        var lines = _fileSystemService.ReadLines(file).ToList();
                        for (int i = 0; i < lines.Count; i++)
                        {
                            bool isMatch;
                            if (regex != null)
                                isMatch = regex.IsMatch(lines[i]);
                            else
                                isMatch = lines[i].Contains(SearchPattern, comparison);

                            if (isMatch)
                            {
                                matchCount++;
                                var relativePath = Path.GetRelativePath(FolderPath, file);
                                var result = new SearchResult
                                {
                                    FilePath = file,
                                    RelativePath = relativePath,
                                    LineNumber = i + 1,
                                    LineText = lines[i].TrimStart(),
                                    FileName = Path.GetFileName(file)
                                };

                                _dispatcherService.Invoke(() => Results.Add(result));
                            }
                        }
                    }
                    catch { /* skip unreadable */ }
                }

                _dispatcherService.Invoke(() =>
                    StatusText = $"Found {matchCount} match(es) in {files.Count} file(s)");
            });
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
