using System.Net.Sockets;
using JobsPulse.Discovery.Models;

namespace JobsPulse.Discovery.Infrastructure;

/// <summary>Tells «the index did not answer» from «the code is broken» - only the former is worth skipping over.</summary>
public static class CrawlIndexFailure
{
    public static bool IsTransient(Exception ex) => ex switch
    {
        OperationCanceledException => false,
        CrawlIndexUnavailableException => true,
        ParquetIndexUnavailableException => true,
        HttpRequestException => true,
        SocketException => true,
        IOException => true,
        TimeoutException => true,
        _ => false
    };

    /// <summary>Short one-line reason for logs - the full exception goes to the log as an attachment anyway.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        CrawlIndexUnavailableException unavailable => unavailable.Failure,
        ParquetIndexUnavailableException unavailable => unavailable.Failure,
        HttpRequestException http => $"{http.HttpRequestError}: {InnermostMessage(http)}",
        _ => $"{ex.GetType().Name}: {InnermostMessage(ex)}"
    };

    private static string InnermostMessage(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null)
            current = current.InnerException;

        return current.Message;
    }
}
