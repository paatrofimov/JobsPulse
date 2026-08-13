using Microsoft.Extensions.Options;

namespace JobsPulse.Tests.Integration.HeadHunter;

/// <summary>One fixed options instance - the tests configure a source by handing it the object, not a config file.</summary>
public sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable OnChange(Action<T, string?> listener) => new Subscription();

    private sealed class Subscription : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
