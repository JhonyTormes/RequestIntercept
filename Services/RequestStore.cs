using System.Collections.Concurrent;
using RequestIntercept.Models;

namespace RequestIntercept.Services;

public class RequestStore
{
    private readonly ConcurrentQueue<InterceptedRequest> _requests = new();
    private const int MaxItems = 500;
    private volatile bool _paused;

    public bool IsPaused
    {
        get => _paused;
        set => _paused = value;
    }

    public void Add(InterceptedRequest request)
    {
        if (_paused) return;
        _requests.Enqueue(request);
        while (_requests.Count > MaxItems && _requests.TryDequeue(out _)) { }
    }

    public List<InterceptedRequest> GetAll() => _requests.Reverse().ToList();

    public InterceptedRequest? GetById(Guid id) => _requests.FirstOrDefault(r => r.Id == id);

    public void Clear()
    {
        while (_requests.TryDequeue(out _)) { }
    }
}
