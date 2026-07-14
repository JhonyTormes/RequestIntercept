namespace RequestIntercept.Models;

public class BreakpointItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public string Host { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int TimeoutSeconds { get; set; } = 120;
}
