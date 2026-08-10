using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Infrastructure;

public static class LoggingHttpClientFactoryExtensions
{
    /// <summary>Named client from the factory, wrapped so its traffic is visible in the log.</summary>
    public static LoggingHttpClient CreateLoggingClient(this IHttpClientFactory factory, string name, ILog log) =>
        new(factory.CreateClient(name), log, name);
}
