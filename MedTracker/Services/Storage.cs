using Microsoft.JSInterop;
using System.Text.Json;
using MedTracker.Domain;

namespace MedTracker.Services;

public sealed class PersistedState
{
    public List<Medication> Medications { get; set; } = new();
    public List<DoseEvent> DoseEvents { get; set; } = new();
}

public sealed class Storage
{
    private const string Key = "state";
    private readonly IJSRuntime _js;
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public Storage(IJSRuntime js) => _js = js;

    public async Task<PersistedState> LoadAsync()
    {
        var raw = await _js.InvokeAsync<string?>("medtracker.kvGet", Key);
        if (string.IsNullOrEmpty(raw)) return new PersistedState();
        try
        {
            return JsonSerializer.Deserialize<PersistedState>(raw, Json) ?? new PersistedState();
        }
        catch
        {
            return new PersistedState();
        }
    }

    public async Task SaveAsync(PersistedState state)
    {
        var raw = JsonSerializer.Serialize(state, Json);
        await _js.InvokeVoidAsync("medtracker.kvPut", Key, raw);
    }
}
