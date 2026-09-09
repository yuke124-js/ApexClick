namespace ApexClick.Services.Capture;

public sealed class RawInputService : IDisposable
{
    public bool IsActive { get; private set; }

    public event Action<int, int>? OnRawMouseDelta;

    public void Start(nint windowHandle)
    {
        
        throw new NotImplementedException();
    }

    public void Stop()
    {
        
        IsActive = false;
    }

    public void Dispose() => Stop();
}
