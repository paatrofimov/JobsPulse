using System.Net;

namespace JobsPulse.Tests.Integration.HeadHunter;

/// <summary>What the stubbed api answers one request with.</summary>
public sealed record HeadHunterStubAnswer(HttpStatusCode Status, string Body)
{
    public static HeadHunterStubAnswer Json(string body) => new(HttpStatusCode.OK, body);

    public static HeadHunterStubAnswer Error(HttpStatusCode status, string body = "{}") => new(status, body);
}
