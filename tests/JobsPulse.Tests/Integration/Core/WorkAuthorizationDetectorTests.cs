using FluentAssertions;
using JobsPulse.Core.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Core;

/// <summary>
/// A vacancy wanting an existing work authorization is dropped, one helping with a visa or a relocation is kept -
/// even when it states a requirement somewhere else.
/// </summary>
public sealed class WorkAuthorizationDetectorTests
{
    [TestCase("Please note that CoStar Group is not able to provide visa sponsorship for this position.")]
    [TestCase("We welcome all qualified candidates who are currently eligible to work full-time in the United States to apply.")]
    [TestCase("Candidates must be legally authorised to work in Australia on a permanent basis without any restrictions.")]
    [TestCase("This role is open only for candidates with indefinite right to work within the USA.")]
    [TestCase("We are unable to provide sponsorship for this role presently or in the future.")]
    [TestCase("Available for Work Visa Sponsorship? No")]
    [TestCase("This position requires an active security clearance.")]
    public void RequiresExisting_should_see_a_requirement(string text)
    {
        WorkAuthorizationDetector.RequiresExisting(text).Should().BeTrue();
    }

    [TestCase("We are keen to support candidates who require visa sponsorship. Please let us know which visa you need.")]
    [TestCase("Candidates must be eligible to work in the UK. Visa sponsorship is available for this role.")]
    [TestCase("You must have the right to work in the Netherlands. We offer a relocation package.")]
    [TestCase("GEICO will consider sponsoring a new qualified applicant for employment authorization for this position.")]
    public void RequiresExisting_should_keep_an_offer_of_help(string text)
    {
        WorkAuthorizationDetector.RequiresExisting(text).Should().BeFalse();
    }

    [TestCase("We do not discriminate on the basis of race, citizenship, national origin or veteran status.")]
    [TestCase("Background checks include proof of work authorization and identity verification.")]
    [TestCase("Government Clearance Required? No")]
    [TestCase("This position is not eligible for relocation. We sponsor certification exams.")]
    public void RequiresExisting_should_ignore_what_is_not_a_requirement(string text)
    {
        WorkAuthorizationDetector.RequiresExisting(text).Should().BeFalse();
    }

    [Test]
    public void RequiresExisting_should_not_take_a_negated_offer_as_help()
    {
        WorkAuthorizationDetector.RequiresExisting(
                "This role is not eligible for relocation assistance. We will not sponsor visas.")
            .Should().BeTrue();
    }
}
