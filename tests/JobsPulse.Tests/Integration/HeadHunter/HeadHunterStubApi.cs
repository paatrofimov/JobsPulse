using System.Text;

namespace JobsPulse.Tests.Integration.HeadHunter;

/// <summary>
/// The api as a function from a url to an answer. Paging, an employer the catalog does not know and a refused request
/// are all shapes of the same contract, and none of them can be provoked against the live platform on demand.
/// </summary>
public sealed class HeadHunterStubApi(Func<Uri, HeadHunterStubAnswer> responder) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request.RequestUri!);

        var answer = responder(request.RequestUri!);

        return Task.FromResult(new HttpResponseMessage(answer.Status)
        {
            Content = new StringContent(answer.Body, Encoding.UTF8, "application/json")
        });
    }

    /// <summary>The query value a request carried, so a test can assert on how the api was asked.</summary>
    public string? QueryOf(int requestIndex, string parameter)
    {
        var query = System.Web.HttpUtility.ParseQueryString(Requests[requestIndex].Query);

        return query[parameter];
    }
}
