using MedTracker.Domain;

namespace MedTracker.Services;

/// In-memory snapshot of all persisted data, with change notifications.
/// The scheduler mutates this and calls SaveAsync + NotifyChanged; UI subscribes.
public sealed class AppState
{
    public List<Medication> Medications { get; private set; } = new();
    public List<DoseEvent> DoseEvents { get; private set; } = new();

    public event Action? Changed;

    private readonly Storage _storage;
    private bool _loaded;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public AppState(Storage storage) => _storage = storage;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        var s = await _storage.LoadAsync();
        Medications = s.Medications;
        DoseEvents = s.DoseEvents;
        _loaded = true;
    }

    public async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            await _storage.SaveAsync(new PersistedState
            {
                Medications = Medications,
                DoseEvents = DoseEvents
            });
        }
        finally { _saveLock.Release(); }
    }

    public void NotifyChanged() => Changed?.Invoke();
}
