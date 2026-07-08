namespace RequestIntercept.Models;

public class InterceptedRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required string Method { get; set; }
    public required string Url { get; set; }
    public required string Host { get; set; }
    public Dictionary<string, string[]> RequestHeaders { get; set; } = [];
    public string? RequestBody { get; set; }
    public int? StatusCode { get; set; }
    public Dictionary<string, string[]> ResponseHeaders { get; set; } = [];
    public string? ResponseBody { get; set; }
    public long DurationMs { get; set; }
    public bool IsHttps { get; set; }
    public string? Error { get; set; }
}
