namespace LightflowStudio;

internal sealed class TrimEditorCloseLifecycle
{
    private readonly Func<ValueTask> _release;
    private readonly object _gate = new();
    private Task<bool?>? _completion;

    public TrimEditorCloseLifecycle(Func<ValueTask> release) => _release = release;

    public Task<bool?> CloseAsync(bool? requestedResult)
    {
        lock (_gate)
            return _completion ??= ReleaseAndPreserveResultAsync(requestedResult);
    }

    private async Task<bool?> ReleaseAndPreserveResultAsync(bool? requestedResult)
    {
        await _release().ConfigureAwait(true);
        return requestedResult;
    }
}
