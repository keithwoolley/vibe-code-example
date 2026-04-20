using Microsoft.JSInterop;
using MedTracker.Domain;

namespace MedTracker.Services;

/// Tracks the one currently-ringing DoseEvent plus a queue of further due events.
/// Pages listen to Changed to show/hide the modal.
public sealed class AlarmService
{
    private readonly IJSRuntime _js;
    private readonly AppState _state;
    private readonly Queue<string> _queue = new();

    public DoseEvent? Current { get; private set; }
    public Medication? CurrentMedication { get; private set; }
    public event Action? Changed;

    public AlarmService(IJSRuntime js, AppState state)
    {
        _js = js;
        _state = state;
    }

    public async Task RingAsync(DoseEvent ev)
    {
        if (Current is not null && Current.Id != ev.Id)
        {
            if (!_queue.Contains(ev.Id)) _queue.Enqueue(ev.Id);
            return;
        }
        if (Current?.Id == ev.Id) return;

        Current = ev;
        CurrentMedication = _state.Medications.FirstOrDefault(m => m.Id == ev.MedicationId);
        await _js.InvokeVoidAsync("medtracker.alarmStart");
        Changed?.Invoke();
    }

    public async Task ResolveAsync(DoseStatus resolution)
    {
        if (Current is null) return;
        if (resolution is not (DoseStatus.Taken or DoseStatus.Ignored))
            throw new ArgumentException("Resolution must be Taken or Ignored.");

        Current.Status = resolution;
        await _js.InvokeVoidAsync("medtracker.alarmStop");
        Current = null;
        CurrentMedication = null;
        await _state.SaveAsync();
        _state.NotifyChanged();
        Changed?.Invoke();

        // Move to next queued event if one exists and is still due.
        while (_queue.Count > 0)
        {
            var nextId = _queue.Dequeue();
            var next = _state.DoseEvents.FirstOrDefault(e => e.Id == nextId);
            if (next is not null && next.Status == DoseStatus.Due)
            {
                await RingAsync(next);
                return;
            }
        }
    }
}
