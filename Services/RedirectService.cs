using RequestIntercept.Models;

namespace RequestIntercept.Services;

public class RedirectService
{
    private readonly List<RedirectRule> _rules = [];
    private readonly object _lock = new();
    private bool _enabled;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public IReadOnlyList<RedirectRule> Rules
    {
        get { lock (_lock) return _rules.ToList(); }
    }

    public void SetRules(List<RedirectRule> rules)
    {
        lock (_lock)
        {
            _rules.Clear();
            _rules.AddRange(rules.Where(r => !string.IsNullOrWhiteSpace(r.From)));
        }
    }

    /// <summary>
    /// Rewrites the given URL if it matches a redirect rule (first match wins).
    /// Returns null when no rule matches (or redirect is disabled).
    /// </summary>
    public string? Rewrite(string url)
    {
        if (!_enabled) return null;
        lock (_lock)
        {
            if (_rules.Count == 0) return null;
            var lower = url;
            foreach (var rule in _rules)
            {
                var idx = lower.IndexOf(rule.From, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    return lower[..idx] + rule.To + lower[(idx + rule.From.Length)..];
                }
            }
        }
        return null;
    }
}