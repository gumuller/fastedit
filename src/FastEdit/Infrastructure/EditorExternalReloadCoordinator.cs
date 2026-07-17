using System.Diagnostics;
using System.IO;
using FastEdit.ViewModels;

namespace FastEdit.Infrastructure;

internal sealed class EditorExternalReloadCoordinator : IDisposable
{
    private readonly TimeSpan _settleDelay;
    private readonly Func<string, Task<string>> _readFileAsync;
    private readonly Func<Action, Task> _dispatchAsync;
    private readonly Func<string, EditorTabViewModel?> _resolveTab;
    private readonly Func<EditorTabViewModel, bool> _confirmDiscard;
    private readonly Action<EditorTabViewModel> _onDiscardDeclined;
    private readonly Action<EditorTabViewModel> _onBufferChanged;
    private readonly Action<string> _onReloaded;
    private readonly Action<string, Exception> _onReadFailed;
    private readonly Func<string, bool> _canReload;
    private readonly ExternalChangeTracker _changeTracker = new();
    private readonly object _sync = new();
    private CancellationTokenSource? _reloadCts;
    private int _processing;
    private bool _disposed;

    public EditorExternalReloadCoordinator(
        TimeSpan settleDelay,
        Func<string, Task<string>> readFileAsync,
        Func<Action, Task> dispatchAsync,
        Func<string, EditorTabViewModel?> resolveTab,
        Func<EditorTabViewModel, bool> confirmDiscard,
        Action<EditorTabViewModel> onDiscardDeclined,
        Action<EditorTabViewModel> onBufferChanged,
        Action<string> onReloaded,
        Action<string, Exception> onReadFailed,
        Func<string, bool> canReload)
    {
        if (settleDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(settleDelay));

        _settleDelay = settleDelay;
        _readFileAsync = readFileAsync ?? throw new ArgumentNullException(nameof(readFileAsync));
        _dispatchAsync = dispatchAsync ?? throw new ArgumentNullException(nameof(dispatchAsync));
        _resolveTab = resolveTab ?? throw new ArgumentNullException(nameof(resolveTab));
        _confirmDiscard = confirmDiscard ?? throw new ArgumentNullException(nameof(confirmDiscard));
        _onDiscardDeclined = onDiscardDeclined ?? throw new ArgumentNullException(nameof(onDiscardDeclined));
        _onBufferChanged = onBufferChanged ?? throw new ArgumentNullException(nameof(onBufferChanged));
        _onReloaded = onReloaded ?? throw new ArgumentNullException(nameof(onReloaded));
        _onReadFailed = onReadFailed ?? throw new ArgumentNullException(nameof(onReadFailed));
        _canReload = canReload ?? throw new ArgumentNullException(nameof(canReload));
    }

    public Task NotifyAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        lock (_sync)
        {
            if (_disposed)
                return Task.CompletedTask;
        }

        _changeTracker.Record(filePath);
        return Interlocked.CompareExchange(ref _processing, 1, 0) == 0
            ? ProcessChangesAsync(filePath)
            : Task.CompletedTask;
    }

    public void Cancel()
    {
        _changeTracker.Invalidate();
        CancellationTokenSource? reloadCts;
        lock (_sync)
        {
            reloadCts = _reloadCts;
            _reloadCts = null;
        }

        reloadCts?.Cancel();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        Cancel();
    }

    private async Task ProcessChangesAsync(string filePath)
    {
        var reloadCts = new CancellationTokenSource();
        lock (_sync)
        {
            if (_disposed)
            {
                reloadCts.Dispose();
                Volatile.Write(ref _processing, 0);
                return;
            }

            _reloadCts = reloadCts;
        }

        var cancellationToken = reloadCts.Token;
        var promptDeclined = false;
        ExternalReloadDecision? approvedDecision = null;

        try
        {
            while (true)
            {
                await Task.Delay(_settleDelay, cancellationToken);

                var change = _changeTracker.Capture(filePath);
                filePath = change.FilePath;
                if (!await InvokeAsync(() => _canReload(filePath)))
                    break;

                var decision = approvedDecision is { } approval &&
                               string.Equals(
                                   approval.ViewModel?.FilePath,
                                   filePath,
                                   StringComparison.OrdinalIgnoreCase)
                    ? approval
                    : await GetDecisionAsync(filePath);

                if (decision.WasPrompted && decision.ShouldReload)
                    approvedDecision = decision;

                if (decision.ShouldReload)
                {
                    var content = await _readFileAsync(filePath);
                    cancellationToken.ThrowIfCancellationRequested();

                    var applyResult = await TryApplyAsync(change, decision, filePath, content, cancellationToken);
                    if (!applyResult.GenerationCurrent)
                        continue;
                    if (!applyResult.ContentApplied)
                        break;

                    approvedDecision = null;
                }

                if (decision.WasPrompted && !decision.ShouldReload)
                {
                    promptDeclined = true;
                    _changeTracker.TakePendingPath();
                    break;
                }

                var pendingPath = _changeTracker.TakePendingPath();
                if (pendingPath == null)
                    break;

                filePath = pendingPath;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException ex)
        {
            Trace.TraceWarning($"Auto-reload skipped for '{filePath}': {ex.Message}");
            await _dispatchAsync(() => _onReadFailed(filePath, ex));
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning($"Auto-reload skipped for '{filePath}': {ex.Message}");
            await _dispatchAsync(() => _onReadFailed(filePath, ex));
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_reloadCts, reloadCts))
                    _reloadCts = null;
            }

            reloadCts.Dispose();
            Volatile.Write(ref _processing, 0);

            var pendingPath = _changeTracker.TakePendingPath();
            if (!promptDeclined &&
                pendingPath != null &&
                await InvokeAsync(() => _canReload(pendingPath)))
            {
                await NotifyAsync(pendingPath);
            }
        }
    }

    private async Task<ExternalReloadDecision> GetDecisionAsync(string filePath)
    {
        var decision = default(ExternalReloadDecision);
        await _dispatchAsync(() =>
        {
            var viewModel = _resolveTab(filePath);
            if (viewModel == null)
                return;

            if (!viewModel.IsModified)
            {
                decision = new ExternalReloadDecision(
                    WasPrompted: false,
                    ShouldReload: true,
                    viewModel,
                    viewModel.Content);
                return;
            }

            var shouldReload = _confirmDiscard(viewModel);
            decision = new ExternalReloadDecision(
                WasPrompted: true,
                ShouldReload: shouldReload,
                viewModel,
                viewModel.Content);
            if (!shouldReload)
                _onDiscardDeclined(viewModel);
        });
        return decision;
    }

    private async Task<ExternalReloadApplyResult> TryApplyAsync(
        ExternalChangeSnapshot change,
        ExternalReloadDecision decision,
        string filePath,
        string content,
        CancellationToken cancellationToken)
    {
        var result = default(ExternalReloadApplyResult);
        await _dispatchAsync(() =>
        {
            var contentApplied = false;
            var generationCurrent = _changeTracker.TryApply(change.Generation, () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_canReload(filePath))
                    return;

                var currentViewModel = _resolveTab(filePath);
                if (currentViewModel == null || !ReferenceEquals(currentViewModel, decision.ViewModel))
                    return;

                if (!string.Equals(currentViewModel.Content, decision.ContentSnapshot, StringComparison.Ordinal))
                {
                    _onBufferChanged(currentViewModel);
                    return;
                }

                currentViewModel.ReplaceContentFromDisk(content);
                contentApplied = true;
                _onReloaded(filePath);
            });
            result = new ExternalReloadApplyResult(generationCurrent, contentApplied);
        });
        return result;
    }

    private async Task<T> InvokeAsync<T>(Func<T> action)
    {
        var result = default(T)!;
        await _dispatchAsync(() => result = action());
        return result;
    }

    private readonly record struct ExternalReloadDecision(
        bool WasPrompted,
        bool ShouldReload,
        EditorTabViewModel? ViewModel,
        string? ContentSnapshot);

    private readonly record struct ExternalReloadApplyResult(
        bool GenerationCurrent,
        bool ContentApplied);
}
