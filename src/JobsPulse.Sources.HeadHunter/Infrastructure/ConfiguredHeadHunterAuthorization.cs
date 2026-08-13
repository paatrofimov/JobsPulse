using JobsPulse.Sources.HeadHunter.Abstractions;
using JobsPulse.Sources.HeadHunter.Options;
using Microsoft.Extensions.Options;

namespace JobsPulse.Sources.HeadHunter.Infrastructure;

/// <summary>
/// The default authorization: whatever `Sources:HeadHunter:AccessToken` holds, which in a normal installation is
/// nothing. A token pasted there is sent as-is - that covers an application token obtained out of band - and a real
/// token acquisition flow can replace this registration without any other code knowing.
/// </summary>
public sealed class ConfiguredHeadHunterAuthorization(IOptionsMonitor<HeadHunterOptions> options)
    : IHeadHunterAuthorization
{
    public ValueTask<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        var token = options.CurrentValue.AccessToken;

        return ValueTask.FromResult(string.IsNullOrWhiteSpace(token) ? null : token.Trim());
    }
}
