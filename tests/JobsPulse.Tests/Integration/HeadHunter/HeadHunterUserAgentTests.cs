using FluentAssertions;
using JobsPulse.Sources.HeadHunter.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.HeadHunter;

/// <summary>
/// The user agent is the one header that can fail every request of the process at once, and a placeholder one does
/// exactly that - the api blacklists the contacts of its own examples.
/// </summary>
public sealed class HeadHunterUserAgentTests
{
    [TestCase("JobsPulse/0.1 (jobs-pulse@example.com)")]
    [TestCase("my-app/1.0 (mail@example.com)")]
    [TestCase("Sample/1.0 (YourCompany)")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void Resolve_should_replace_an_agent_the_api_would_refuse(string? configured)
    {
        HeadHunterUserAgent.IsAcceptable(configured).Should().BeFalse();
        HeadHunterUserAgent.Resolve(configured).Should().Be(HeadHunterUserAgent.Default);
    }

    [Test]
    public void Resolve_should_keep_a_configured_agent_that_names_a_real_contact()
    {
        const string configured = "JobsPulse/1.0 (patrofimov@yandex.ru)";

        HeadHunterUserAgent.IsAcceptable(configured).Should().BeTrue();
        HeadHunterUserAgent.Resolve($"  {configured}  ").Should().Be(configured);
    }

    /// <summary>The default is what a fresh installation sends, so it has to pass the check it exists for.</summary>
    [Test]
    public void Default_should_be_acceptable() =>
        HeadHunterUserAgent.IsAcceptable(HeadHunterUserAgent.Default).Should().BeTrue();
}
