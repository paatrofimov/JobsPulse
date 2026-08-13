namespace JobsPulse.Sources.HeadHunter.Abstractions;

/// <summary>
/// Where the bearer token of an api request comes from, or null when there is none. Everything this source does -
/// employer search, employer lookup, vacancy search, vacancy detail - is public and works unauthorized, so the default
/// implementation returns null and no OAuth flow exists anywhere in the process.
///
/// It is an abstraction rather than a token string because the two tokens HeadHunter can issue are acquired
/// differently: an application token is a client-credentials call that has to be refreshed on a timer, and a user
/// token is an authorization-code flow bound to one person. Both fit behind this method; neither has to exist for the
/// source to work.
/// </summary>
public interface IHeadHunterAuthorization
{
    ValueTask<string?> GetAccessTokenAsync(CancellationToken ct);
}
