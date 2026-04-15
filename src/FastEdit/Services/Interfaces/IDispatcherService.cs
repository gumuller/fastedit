namespace FastEdit.Services.Interfaces;

public interface IDispatcherService
{
    /// <summary>Invoke an action on the UI thread.</summary>
    void Invoke(Action action);

    /// <summary>Invoke an action on the UI thread asynchronously.</summary>
    Task InvokeAsync(Action action);

    /// <summary>Invoke a func on the UI thread asynchronously.</summary>
    Task<T> InvokeAsync<T>(Func<T> func);

    /// <summary>Check if we're on the UI thread.</summary>
    bool CheckAccess();
}
