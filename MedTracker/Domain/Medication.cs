namespace MedTracker.Domain;

public sealed class Medication
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? DoseAmount { get; set; }
    public string? DoseUnit { get; set; }
    public Form? Form { get; set; }
    public string? Notes { get; set; }
    public bool IsPRN { get; set; }
    public List<string> AlarmTimes { get; set; } = new();

    public string ScheduleSummary() =>
        IsPRN ? "PRN" : string.Join(", ", AlarmTimes.OrderBy(t => t));

    public string DoseSummary()
    {
        var amount = (DoseAmount ?? "").Trim();
        var unit = (DoseUnit ?? "").Trim();
        if (amount.Length == 0 && unit.Length == 0) return "";
        return (amount + " " + unit).Trim();
    }

    public static string? Validate(string name, bool isPRN, List<string> alarmTimes)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name is required.";
        if (isPRN && alarmTimes.Count > 0)
            return "PRN medications cannot have alarm times.";
        if (!isPRN && alarmTimes.Count == 0)
            return "Scheduled medications need at least one alarm time.";
        var trimmed = alarmTimes.Select(t => t.Trim()).ToList();
        foreach (var t in trimmed)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(t, @"^([01]\d|2[0-3]):[0-5]\d$"))
                return $"Invalid time '{t}'. Use HH:MM (00:00–23:59).";
        }
        if (trimmed.Distinct().Count() != trimmed.Count)
            return "Duplicate alarm times are not allowed.";
        return null;
    }
}
