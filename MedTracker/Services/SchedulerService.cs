using MedTracker.Domain;

namespace MedTracker.Services;

/// Runs rollover on start + each local-midnight crossing, and promotes pending
/// events to due when their scheduled time is reached.
public sealed class SchedulerService : IAsyncDisposable
{
    private readonly AppState _state;
    private readonly AlarmService _alarms;
    private PeriodicTimer? _timer;
    private Task? _loop;
    private CancellationTokenSource? _cts;
    private DateOnly _lastSeenDate;
    private bool _started;

    public SchedulerService(AppState state, AlarmService alarms)
    {
        _state = state;
        _alarms = alarms;
    }

    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;

        await _state.EnsureLoadedAsync();
        _lastSeenDate = DateOnly.FromDateTime(DateTime.Now);

        RolloverAndMaterialize();
        await _state.SaveAsync();
        _state.NotifyChanged();

        // Ring any past-time pending events (scheduledTime <= now on today).
        await FireDuePendingAsync();

        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        _loop = RunLoopAsync(_cts.Token);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(ct))
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                if (today != _lastSeenDate)
                {
                    _lastSeenDate = today;
                    RolloverAndMaterialize();
                    await _state.SaveAsync();
                    _state.NotifyChanged();
                }
                await FireDuePendingAsync();
            }
        }
        catch (OperationCanceledException) { }
    }

    /// Mark pre-today pending/due as ignored, materialize today's pending events,
    /// and delete events older than 30 days (today + 29 prior).
    public void RolloverAndMaterialize()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var todayStr = today.ToString("yyyy-MM-dd");

        foreach (var e in _state.DoseEvents)
        {
            if (string.Compare(e.Date, todayStr, StringComparison.Ordinal) < 0
                && e.Status is DoseStatus.Pending or DoseStatus.Due)
            {
                e.Status = DoseStatus.Ignored;
            }
        }

        foreach (var med in _state.Medications.Where(m => !m.IsPRN))
        {
            foreach (var time in med.AlarmTimes)
            {
                bool exists = _state.DoseEvents.Any(e =>
                    e.MedicationId == med.Id &&
                    e.Date == todayStr &&
                    e.ScheduledTime == time);
                if (!exists)
                {
                    _state.DoseEvents.Add(new DoseEvent
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        MedicationId = med.Id,
                        Date = todayStr,
                        ScheduledTime = time,
                        Status = DoseStatus.Pending
                    });
                }
            }
        }

        var cutoff = today.AddDays(-29).ToString("yyyy-MM-dd");
        _state.DoseEvents.RemoveAll(e =>
            string.Compare(e.Date, cutoff, StringComparison.Ordinal) < 0);
    }

    private async Task FireDuePendingAsync()
    {
        var todayStr = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        var nowHm = DateTime.Now.ToString("HH:mm");

        var due = _state.DoseEvents
            .Where(e => e.Date == todayStr
                        && e.Status == DoseStatus.Pending
                        && e.ScheduledTime is not null
                        && string.Compare(e.ScheduledTime, nowHm, StringComparison.Ordinal) <= 0)
            .OrderBy(e => e.ScheduledTime, StringComparer.Ordinal)
            .ToList();

        if (due.Count == 0) return;

        foreach (var e in due) e.Status = DoseStatus.Due;
        await _state.SaveAsync();
        _state.NotifyChanged();

        foreach (var e in due)
        {
            await _alarms.RingAsync(e);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch { }
        _timer?.Dispose();
        if (_loop is not null)
        {
            try { await _loop; } catch { }
        }
    }
}
