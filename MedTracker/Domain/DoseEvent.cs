namespace MedTracker.Domain;

public sealed class DoseEvent
{
    public string Id { get; set; } = "";
    public string MedicationId { get; set; } = "";
    public string Date { get; set; } = "";            // YYYY-MM-DD
    public string? ScheduledTime { get; set; }        // HH:MM, or null for PRN
    public DoseStatus Status { get; set; }
}
