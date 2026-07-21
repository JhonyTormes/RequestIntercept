namespace RequestIntercept.Services;

public class BlocklistService
{
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

    public bool IsBlocked(string url)
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
}
