using FluentAssertions;
using JobsPulse.Core.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Core;

/// <summary>A spoken language other than English demanded by an English ad - and only demanded, not wished for.</summary>
public sealed class LanguageRequirementDetectorTests
{
    [TestCase("<ul><li>Fluent in Polish and English</li></ul>")]
    [TestCase("<ul><li>Fluency in German and English are a mandatory requirement</li></ul>")]
    [TestCase("<ul><li>Proficiency in both French and English, written and spoken, is required</li></ul>")]
    [TestCase("<p>Excellent communication skills in English (C1) (German Level C1)</p>")]
    public void RequiresOther_should_see_a_demand(string description)
    {
        LanguageRequirementDetector.RequiresOther(description).Should().BeTrue();
    }

    [TestCase("<ul><li>Fluency in English, French is a plus</li></ul>")]
    [TestCase("<p>Requirements</p><ul><li>C#</li></ul><p>Nice to have</p><ul><li>Fluent German</li></ul>")]
    [TestCase("<ul><li>Excellent English communication skills</li><li>Experience with German clients</li></ul>")]
    public void RequiresOther_should_ignore_a_wish(string description)
    {
        LanguageRequirementDetector.RequiresOther(description).Should().BeFalse();
    }
}
