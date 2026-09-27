using FluentAssertions;
using JobsPulse.Sinks.Telegram.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Telegram;

/// <summary>A filter rule answer: a list replaces, a leading `+` adds, a leading `-` with words removes.</summary>
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

    [Test]
    public void Apply_should_remove_after_a_minus_ignoring_case()
    {
        FilterListEdit.Apply(Current, "- Backend, php").Should().Equal("sre");
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
