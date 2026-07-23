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

public class BreakpointResult
{
    public bool Forward { get; set; }
    public Dictionary<string, string[]>? ModifiedHeaders { get; set; }
    public byte[]? ModifiedBody { get; set; }
}

public class BreakpointEditRequest
{
    public Dictionary<string, string[]>? Headers { get; set; }
    public string? Body { get; set; }
}
