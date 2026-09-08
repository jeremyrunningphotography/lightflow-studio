using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LightflowStudio;

internal enum DescriptionEditOperation { Keep, Set, Clear }
internal sealed record DescriptionEditChoice(DescriptionEditOperation Operation, string Label);

internal abstract class InspectorObservable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

internal sealed class InspectorDescriptionField : InspectorObservable
{
    private string _text;
    private DescriptionEditOperation _operation;
    public AssetDescriptionField Field { get; }
    public string Label => Field switch
    {
        AssetDescriptionField.Description => "Caption / description",
        AssetDescriptionField.CreatorOverride => "Creator override",
        AssetDescriptionField.CreditOverride => "Credit override",
        _ => Field.ToString()
    };
    public bool Multiline => Field is AssetDescriptionField.Description or AssetDescriptionField.Notes;
    public double EditorHeight => Multiline ? 76 : 30;
    public bool IsMixed { get; }
    public string ValueState { get; }
    public bool IsReadOnly => Operation != DescriptionEditOperation.Set;
    public static IReadOnlyList<DescriptionEditChoice> Choices { get; } =
        [new(DescriptionEditOperation.Keep, "Leave unchanged"), new(DescriptionEditOperation.Set, "Set"), new(DescriptionEditOperation.Clear, "Clear")];
    public string Text { get => _text; set { _text = value; Changed(); } }
    public DescriptionEditOperation Operation
    {
        get => _operation;
        set { _operation = value; Changed(); Changed(nameof(IsReadOnly)); }
    }

    public InspectorDescriptionField(AssetDescriptionField field, IReadOnlyCollection<AssetDescription> values)
    {
        Field = field;
        var first = values.First().Get(field);
        IsMixed = values.Any(v => !string.Equals(first, v.Get(field), StringComparison.Ordinal));
        _text = IsMixed ? "" : first ?? "";
        ValueState = IsMixed ? "Mixed values" : first is null ? "Not set" : values.Count > 1 ? "Common value" : "Catalog value";
    }
}

/// <summary>Transient editing state over the existing Inspector selection. No selection ownership or source metadata.</summary>
internal sealed class InspectorDescriptionEditor(IAssetDescriptionStore store) : InspectorObservable, IDisposable
{
    private Guid?[] _context = [];
    private bool _player;
    private long _generation;
    private CancellationTokenSource? _read;
    private IReadOnlyDictionary<Guid, long> _revisions = new Dictionary<Guid, long>();
    private bool _loading, _saving, _ready, _conflict;
    private string _status = "";
    public IReadOnlyList<InspectorDescriptionField> Fields { get; private set; } = [];
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string ApplyLabel => _context.Length > 1 ? $"Apply to {_context.Length:N0} assets" : "Apply changes";
    public bool CanEdit => _ready && !_loading && !_saving;
    public bool CanApply => CanEdit && !_conflict && Fields.Any(f => f.Operation != DescriptionEditOperation.Keep);
    public bool CanReload => !_loading && !_saving && _context.Length > 0 && _context.All(id => id.HasValue);
    public bool HasDraft => Fields.Any(f => f.Operation != DescriptionEditOperation.Keep);

    public Task SetContextAsync(IEnumerable<Guid?> ids, bool player)
    {
        // Browser sorting may reorder the same selection; it must not discard an editing draft.
        var context = ids.Distinct().OrderBy(id => id).ToArray();
        if (_player == player && context.SequenceEqual(_context)) return Task.CompletedTask;
        var discarded = HasDraft;
        _context = context;
        _player = player;
        Changed(nameof(ApplyLabel));
        return LoadAsync(discarded ? "Unapplied changes discarded after the media context changed." : "");
    }

    public Task ReloadAsync() => LoadAsync("Values reloaded; unapplied changes discarded.");

    private async Task LoadAsync(string message)
    {
        var generation = ++_generation;
        _read?.Cancel(); _read?.Dispose(); _read = new();
        var token = _read.Token;
        foreach (var field in Fields) field.PropertyChanged -= FieldChanged;
        Fields = [];
        _revisions = new Dictionary<Guid, long>();
        _ready = false; _conflict = false; _loading = true;
        Status = message;
        NotifyState(); Changed(nameof(Fields));
        var ids = _context.Where(id => id.HasValue).Select(id => id!.Value).ToArray();
        try
        {
            if (ids.Length == 0 || ids.Length != _context.Length)
            {
                Status = _context.Length == 0 ? "Select Catalog assets to edit descriptions." : "All selected assets must have Catalog identities to edit descriptions.";
                return;
            }
            var values = await store.GetAsync(ids, token);
            if (generation != _generation || token.IsCancellationRequested) return;
            if (values.Count != ids.Length) throw new AssetDescriptionConflictException();
            _revisions = values.ToDictionary(p => p.Key, p => p.Value.Revision);
            Fields = Enum.GetValues<AssetDescriptionField>().Select(f => new InspectorDescriptionField(f, values.Values.ToArray())).ToArray();
            foreach (var field in Fields) field.PropertyChanged += FieldChanged;
            _ready = true;
            Changed(nameof(Fields));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (generation == _generation) Status = $"Descriptions unavailable: {exception.Message}";
        }
        finally
        {
            if (generation == _generation) { _loading = false; NotifyState(); }
        }
    }

    public async Task ApplyAsync()
    {
        if (!CanApply) return;
        var generation = _generation;
        var count = _revisions.Count;
        var patch = new AssetDescriptionPatch(Fields.Where(f => f.Operation != DescriptionEditOperation.Keep)
            .ToDictionary(f => f.Field, f => f.Operation == DescriptionEditOperation.Clear ? null : f.Text));
        var expected = _revisions;
        _saving = true;
        Status = $"Saving descriptions for {count:N0} asset(s)…";
        NotifyState();
        try
        {
            // Explicit Apply captures the original targets. Navigation cannot retarget this transaction.
            await store.ApplyAsync(expected, patch);
            if (generation == _generation) await LoadAsync($"Saved descriptions for {count:N0} asset(s).");
            else Status = $"Saved descriptions for the previous {count:N0} asset(s).";
        }
        catch (Exception exception)
        {
            if (generation == _generation)
            {
                _conflict = exception is AssetDescriptionConflictException;
                Status = $"Descriptions were not saved. {exception.Message}";
            }
            else Status = $"Descriptions for the previous {count:N0} asset(s) were not saved. {exception.Message}";
        }
        finally { _saving = false; NotifyState(); }
    }

    private void FieldChanged(object? sender, PropertyChangedEventArgs e) => Changed(nameof(CanApply));
    private void NotifyState() { Changed(nameof(CanEdit)); Changed(nameof(CanApply)); Changed(nameof(CanReload)); }
    public void Dispose() { ++_generation; _read?.Cancel(); _read?.Dispose(); }
}
