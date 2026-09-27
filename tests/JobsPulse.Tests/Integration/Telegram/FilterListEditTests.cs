using FluentAssertions;
using JobsPulse.Sinks.Telegram.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Telegram;

/// <summary>A filter rule answer: a list replaces, a leading `+` or `-` with words adds, a lone `-` clears.</summary>
public sealed class FilterListEditTests
{
    private static readonly string[] Current = ["backend", "sre"];

    [Test]
    public void Apply_should_replace_with_a_plain_list()
    {
        FilterListEdit.Apply(Current, "go, rust").Should().Equal("go", "rust");
    }

    [Test]
    public void Apply_should_add_after_a_plus_without_duplicates()
    {
        FilterListEdit.Apply(Current, "+ go, SRE, rust").Should().Equal("backend", "sre", "go", "rust");
    }

    // The answer that once removed nothing and changed nothing: `-` before the words of an «excluded» rule.
    [Test]
    public void Apply_should_add_after_a_minus_followed_by_words()
    {
        FilterListEdit.Apply(["manager"], "- Bioprocess, Plant, On-Site, Intern")
            .Should().Equal("manager", "Bioprocess", "Plant", "On-Site", "Intern");
    }

    [Test]
    public void Apply_should_clear_with_a_lone_minus()
    {
        FilterListEdit.Apply(Current, " - ").Should().BeEmpty();
        FilterListEdit.Apply(Current, "—").Should().BeEmpty();
    }

    [Test]
    public void Apply_should_keep_hyphenated_words_of_a_plain_list()
    {
        FilterListEdit.Apply(Current, "on-site, full-time").Should().Equal("on-site", "full-time");
    }
}
