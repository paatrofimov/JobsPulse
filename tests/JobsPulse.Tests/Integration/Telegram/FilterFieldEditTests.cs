using FluentAssertions;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Models;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Telegram;

/// <summary>
/// One answer edits a whole field: plain or `+` words are wanted, `-` words excluded, `=` replaces, a lone `-` clears.
/// </summary>
public sealed class FilterFieldEditTests
{
    private static readonly FilterField Current = new(["backend", "sre"], ["manager"]);

    [Test]
    public void Apply_should_add_wanted_and_excluded_words_by_their_sign()
    {
        var result = FilterFieldEdit.Apply(Current, "go, +rust, -intern");

        result.Wanted.Should().Equal("backend", "sre", "go", "rust");
        result.Excluded.Should().Equal("manager", "intern");
    }

    // The answer that used to change nothing: excluded words, each with a minus.
    [Test]
    public void Apply_should_exclude_every_word_with_a_minus()
    {
        var result = FilterFieldEdit.Apply(Current, "-Bioprocess, -Plant, -On-Site, -Intern");

        result.Wanted.Should().Equal("backend", "sre");
        result.Excluded.Should().Equal("manager", "Bioprocess", "Plant", "On-Site", "Intern");
    }

    [Test]
    public void Apply_should_move_a_word_to_the_other_list()
    {
        var result = FilterFieldEdit.Apply(Current, "-SRE, Manager");

        result.Wanted.Should().Equal("backend", "Manager");
        result.Excluded.Should().Equal("SRE");
    }

    [Test]
    public void Apply_should_replace_the_field_after_an_equals_sign()
    {
        var result = FilterFieldEdit.Apply(Current, "= go, -php");

        result.Wanted.Should().Equal("go");
        result.Excluded.Should().Equal("php");
    }

    [Test]
    public void Apply_should_clear_with_a_lone_minus()
    {
        FilterFieldEdit.Apply(Current, " - ").IsEmpty.Should().BeTrue();
        FilterFieldEdit.Apply(Current, "—").IsEmpty.Should().BeTrue();
    }

    [Test]
    public void Apply_should_keep_hyphens_inside_a_word()
    {
        FilterFieldEdit.Apply(FilterField.Empty, "on-site, full-time").Wanted.Should().Equal("on-site", "full-time");
    }

    [Test]
    public void Render_should_give_an_answer_that_replaces_the_field_with_itself()
    {
        var rendered = FilterFieldEdit.Render(Current);

        rendered.Should().Be("= backend, sre, -manager");

        var roundTrip = FilterFieldEdit.Apply(FilterField.Empty, rendered);
        roundTrip.Wanted.Should().Equal(Current.Wanted);
        roundTrip.Excluded.Should().Equal(Current.Excluded);
    }
}
