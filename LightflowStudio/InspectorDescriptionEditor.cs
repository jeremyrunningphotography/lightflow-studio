using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LightflowStudio;

internal sealed record DescriptionConfirmation(bool IsApply, int AssetCount, IReadOnlyList<string> Fields);

internal abstract class InspectorObservable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

internal sealed class InspectorDescriptionField : InspectorObservable
{
    private string _text;
    private readonly string _originalText;
    private bool _editedMixed;
    public AssetDescriptionField Field { get; }
    public string Label => Field switch
    {
        AssetDescriptionField.Description => "Caption / description",
        AssetDescriptionField.CreatorOverride => "Creator override",
        AssetDescriptionField.CreditOverride => "Credit override",
        _ => Field.ToString()
    };
    public bool Multiline => Field is AssetDescriptionField.Description or AssetDescriptionField.Notes;
    public bool IsMixed { get; }
    private readonly string _valueState;
    public string ValueState => IsDirty ? Text.Length == 0 ? "Will clear on Apply" : "Edited · not applied" : _valueState;
    public bool IsDirty => IsMixed ? _editedMixed : !Equivalent(_originalText, Text);
    private bool Equivalent(string a, string b) => string.Equals(
        Multiline ? a.ReplaceLineEndings("\n") : a, Multiline ? b.ReplaceLineEndings("\n") : b, StringComparison.Ordinal);
    public string Text
    {
        get => _text;
        set
        {
            // Rendering/focus may echo the initial empty mixed presentation. That is not an edit.
            if (string.Equals(_text, value, StringComparison.Ordinal)) return;
            _text = value;
            if (IsMixed) _editedMixed = true;
            Changed(); Changed(nameof(IsDirty)); Changed(nameof(ValueState));
        }
    }

    public InspectorDescriptionField(AssetDescriptionField field, IReadOnlyCollection<AssetDescription> values)
    {
        Field = field;
        var first = values.First().Get(field);
        IsMixed = values.Any(v => !string.Equals(first, v.Get(field), StringComparison.Ordinal));
        _text = IsMixed ? "" : first ?? "";
        _originalText = _text;
        _valueState = IsMixed ? "Mixed values · type to replace" : first is null ? "Not set" : values.Count > 1 ? "Common value" : "Catalog value";
    }
}

/// <summary>Transient editing state over the existing Inspector selection. No selection ownership or source metadata.</summary>
internal sealed class InspectorDescriptionEditor(IAssetDescriptionStore store,
    Func<DescriptionConfirmation, bool> confirm) : InspectorObservable, IDisposable
{
    private Guid?[] _context = [];
    private bool _player;
    private long _generation;
    private CancellationTokenSource? _read;
    private IReadOnlyDictionary<Guid, long> _revisions = new Dictionary<Guid, long>();
    private bool _loading, _saving, _ready, _conflict, _confirming;
    private string _status = "";
    private string _discardNotice = "";
    public IReadOnlyList<InspectorDescriptionField> Fields { get; private set; } = [];
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string ApplyLabel => _context.Length > 1 ? $"Apply to {_context.Length:N0} assets" : "Apply changes";
    public bool CanEdit => _ready && !_loading && !_saving && !_confirming;
    public bool CanApply => CanEdit && !_conflict && HasDraft;
    public bool CanReload => !_loading && !_saving && !_confirming && _context.Length > 0 && _context.All(id => id.HasValue);
    public bool HasDraft => Fields.Any(f => f.IsDirty);

    public Task SetContextAsync(IEnumerable<Guid?> ids, bool player)
    {
        // Browser sorting may reorder the same selection; it must not discard an editing draft.
        var context = ids.Distinct().OrderBy(id => id).ToArray();
        if (_player == player && context.SequenceEqual(_context)) return Task.CompletedTask;
        var discarded = HasDraft;
        if (discarded) _discardNotice = "Unapplied changes discarded after the media context changed.";
        _context = context;
        _player = player;
        Changed(nameof(ApplyLabel));
        return LoadAsync(_discardNotice);
    }

    public Task ReloadAsync()
    {
        if (!CanReload) return Task.CompletedTask;
        var dirty = HasDraft;
        if (dirty && !ConfirmAction(isApply: false)) return Task.CompletedTask;
        _discardNotice = "";
        return LoadAsync(dirty ? "Values reloaded; unapplied changes discarded." : "Values reloaded.");
    }

    private bool ConfirmAction(bool isApply)
    {
        var generation = _generation;
        var request = new DescriptionConfirmation(isApply, _revisions.Count, Fields.Where(f => f.IsDirty).Select(f => f.Label).ToArray());
        _confirming = true;
        NotifyState();
        try { return confirm(request) && generation == _generation; }
        finally { _confirming = false; NotifyState(); }
    }

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
                Status = message + (_context.Length == 0 ? " Select Catalog assets to edit descriptions." : " All selected assets must have Catalog identities to edit descriptions.");
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
            if (generation == _generation) Status = $"{message} Descriptions unavailable: {exception.Message}";
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
        var patch = new AssetDescriptionPatch(Fields.Where(f => f.IsDirty)
            .ToDictionary(f => f.Field, f => f.Text.Length == 0 ? null : f.Text));
        var expected = _revisions;
        try { patch.Validate(); }
        catch (ArgumentException exception) { Status = $"Descriptions were not saved. {exception.Message}"; return; }
        if (!ConfirmAction(isApply: true)) return;
        _saving = true;
        Status = $"Saving descriptions for {count:N0} asset(s)…";
        NotifyState();
        try
        {
            // Explicit Apply captures the original targets. Navigation cannot retarget this transaction.
            await store.ApplyAsync(expected, patch);
            if (generation == _generation)
            {
                _discardNotice = "";
                await LoadAsync($"Saved descriptions for {count:N0} asset(s).");
            }
            else Status = $"{_discardNotice} Saved descriptions for the previous {count:N0} asset(s).";
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

    private void FieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InspectorDescriptionField.Text) && _discardNotice.Length > 0)
        { _discardNotice = ""; Status = ""; }
        Changed(nameof(CanApply));
    }
    private void NotifyState() { Changed(nameof(CanEdit)); Changed(nameof(CanApply)); Changed(nameof(CanReload)); }
    public void Dispose() { ++_generation; _read?.Cancel(); _read?.Dispose(); }
}
