namespace FastEdit.Core.Search;

/// <summary>
/// A result set that never contains more than the caller-provided limit.
/// </summary>
public readonly record struct BoundedSearchResult<T>(
    IReadOnlyList<T> Results,
    bool IsTruncated);
