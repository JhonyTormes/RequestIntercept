using System.Collections.Concurrent;
using RequestIntercept.Models;

namespace RequestIntercept.Services;

public class BreakpointService
{
    private readonly ConcurrentDictionary<Guid, PendingBreakpoint> _pending = new();
    private readonly List<string> _patterns = [];
    private readonly object _lock = new();
    private bool _enabled;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public IReadOnlyList<string> Patterns
    {
        get { lock (_lock) return _patterns.ToList(); }
    }

    public void SetPatterns(List<string> patterns)
    {
        lock (_lock)
        {
            _patterns.Clear();
            _patterns.AddRange(patterns.Select(p => p.ToLowerInvariant()));
        }
    }

    public bool ShouldBreak(string url)
    {
        if (!_enabled) return false;
        lock (_lock)
        {
            if (_patterns.Count == 0) return false;
            if (_patterns.Contains("*")) return true;
            var lower = url.ToLowerInvariant();
            return _patterns.Any(p => lower.Contains(p));
        }
    }

    public PendingBreakpoint Register(string method, string url, string host,
        Dictionary<string, string[]> requestHeaders, string? requestBody, byte[]? rawBody,
        TaskCompletionSource<BreakpointResult> tcs)
    {
        var item = new PendingBreakpoint
        {
            Id = Guid.NewGuid(),
            Method = method,
            Url = url,
            Host = host,
            RequestHeaders = requestHeaders,
            RequestBody = requestBody,
            RawBody = rawBody,
            Timestamp = DateTime.UtcNow,
            Tcs = tcs
        };
        _pending[item.Id] = item;
        return item;
    }

    public PendingBreakpoint? Get(Guid id)
    {
        _pending.TryGetValue(id, out var item);
        return item;
    }

    public List<PendingBreakpoint> GetAll()
    {
        return _pending.Values.OrderByDescending(i => i.Timestamp).ToList();
    }

    public int Count => _pending.Count;

    public void Continue(Guid id, Dictionary<string, string[]>? modifiedHeaders = null, byte[]? modifiedBody = null)
    {
        if (_pending.TryRemove(id, out var item))
        {
            item.Tcs.TrySetResult(new BreakpointResult
            {
                Forward = true,
                ModifiedHeaders = modifiedHeaders,
                ModifiedBody = modifiedBody
            });
        }
    }

    public void Drop(Guid id)
    {
        if (_pending.TryRemove(id, out var item))
        {
            item.Tcs.TrySetResult(new BreakpointResult { Forward = false });
        }
    }

    public void Cleanup()
    {
        var now = DateTime.UtcNow;
        foreach (var (id, item) in _pending)
        {
            if ((now - item.Timestamp).TotalSeconds > 120)
            {
                if (_pending.TryRemove(id, out _))
                {
                    item.Tcs.TrySetResult(new BreakpointResult { Forward = false });
                }
            }
        }
    }
}

public class PendingBreakpoint
{
    public Guid Id { get; set; }
    public required string Method { get; set; }
    public required string Url { get; set; }
    public required string Host { get; set; }
    public Dictionary<string, string[]>? RequestHeaders { get; set; }
    public string? RequestBody { get; set; }
    public byte[]? RawBody { get; set; }
    public DateTime Timestamp { get; set; }
    public required TaskCompletionSource<BreakpointResult> Tcs { get; set; }
}
