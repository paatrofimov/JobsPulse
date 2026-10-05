using FluentAssertions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Models;
using JobsPulse.Sinks.Telegram.Pipeline.Screens;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Telegram;

/// <summary>The digest and the statistics screen share one body; the header says which one it is.</summary>
public sealed class StatsFormatterTests
{
    private static readonly DateTimeOffset To = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Format_should_render_every_number_of_the_digest()
    {
        var html = StatsFormatter.Format(Stats(), BotLanguage.English, digest: true);

        html.Should().Contain("📊 Digest for the last 3 days · Back &amp; end");
        html.Should().Contain("<p>🗓 Changes: September 29, 12:00 – October 02, 12:00 UTC</p>");
        html.Should().Contain("🆕 Vacancies opened: <b>12</b>");
        html.Should().Contain("❌ Vacancies closed: <b>5</b>");
        html.Should().Contain("🏢 New companies with vacancies: <b>2</b><br>");
        html.Should().Contain("🏁 Companies that closed every matching vacancy: <b>1</b></p>");
        html.Should().NotContain("Gamma");
        html.Should().Contain("1. Acme — 10 events (+5 / ✏️3 / ❌2)");
        html.Should().Contain("2. Beta — 1 events (+1 / ✏️0 / ❌0)");
        html.Should().Contain("1. Acme — 7 new<br>2. Delta — 4 new");
    }

    [Test]
    public void Format_should_render_the_screen_header_in_russian()
    {
        var html = StatsFormatter.Format(Stats() with { Days = 14 }, BotLanguage.Russian, digest: false);

        html.Should().Contain("📊 Back &amp; end · за 14 дней");
        html.Should().Contain("🆕 Открылось вакансий: <b>12</b>");
        html.Should().Contain("🏢 Новых компаний с вакансиями: <b>2</b><br>");
        html.Should().Contain("1. Acme — новых: 7");
    }

    [Test]
    public void Format_should_say_nothing_happened_instead_of_an_empty_top()
    {
        var html = StatsFormatter.Format(
            Stats() with { NewCompanies = [], EmptiedCompanies = [], TopByActivity = [], TopByOpened = [] },
            BotLanguage.English,
            digest: false);

        html.Should().Contain("🏢 New companies with vacancies: <b>0</b><br>");
        html.Should().Contain("<b>🔥 Most active companies</b><br>nothing in this period</p>");
        html.Should().Contain("<b>📈 Most new vacancies</b><br>nothing in this period</p>");
    }

    [Test]
    public void Format_should_add_the_year_to_a_period_crossing_new_year()
    {
        var to = new DateTimeOffset(2027, 1, 2, 8, 30, 0, TimeSpan.Zero);

        var html = StatsFormatter.Format(
            Stats() with { From = to.AddDays(-7), To = to, Days = 7 },
            BotLanguage.English,
            digest: false);

        html.Should().Contain("December 26 2026, 08:30 – January 02 2027, 08:30 UTC");
    }

    [Test]
    public void FormatRun_should_put_what_the_run_walked_above_its_changes()
    {
        var stats = Stats() with { From = To.AddMinutes(-45), Days = 0 };

        var html = StatsFormatter.FormatRun(
            new TraversalRunReport(
                TraversalKind.Watchlist,
                new CycleReport(120, 3000, 400, 300, 17, 2, To.AddDays(-2).AddHours(-3)),
                stats),
            BotLanguage.English);

        html.Should().StartWith("<h6>🔄 Polling run · Back &amp; end</h6>");
        html.Should().Contain("<p>⏱ Run: October 02, 11:15 – October 02, 12:00 UTC<br>"
                              + "🗓 Changes: September 30, 09:00 – October 02, 12:00 UTC</p>");
        html.Should().Contain("Boards walked: <b>120</b>, failed: 2, changes: 17");
        html.Should().Contain("🆕 Vacancies opened: <b>12</b>");
    }

    [Test]
    public void FormatRun_should_say_an_unfinished_run_stopped()
    {
        var html = StatsFormatter.FormatRun(
            new TraversalRunReport(TraversalKind.Registry, null, Stats()),
            BotLanguage.Russian);

        html.Should().StartWith("<h6>🗂 Прогон реестра · Back &amp; end</h6>");
        html.Should().Contain("Прогон остановился раньше времени");
    }

    [Test]
    public void FormatRun_should_say_every_company_was_polled_for_the_first_time()
    {
        var html = StatsFormatter.FormatRun(
            new TraversalRunReport(TraversalKind.Watchlist, new CycleReport(3, 30, 10, 5, 5, 0), Stats()),
            BotLanguage.Russian);

        html.Should().Contain("<br>🗓 Изменения: все компании опрошены впервые</p>");
    }

    [Test]
    public void FormatDiscovery_should_name_the_crawl_indexes_it_walked()
    {
        var report = new BoardDiscoveryReport(true, 2, 10, 1, 1, 0)
        {
            FirstCollection = "CC-MAIN-2026-31",
            LastCollection = "CC-MAIN-2026-35"
        };

        var html = StatsFormatter.FormatDiscovery(
            new DiscoveryRunReport(To.AddHours(-5), To, false, report), BotLanguage.English);

        html.Should().Contain("<p>⏱ Run: October 02, 07:00 – October 02, 12:00 UTC<br>"
                              + "🗓 Crawl indexes: CC-MAIN-2026-31 … CC-MAIN-2026-35</p>");
    }

    [Test]
    public void FormatDiscovery_should_render_what_was_mined()
    {
        var html = StatsFormatter.FormatDiscovery(
            new DiscoveryRunReport(To.AddHours(-5), To, true, new BoardDiscoveryReport(true, 12, 1_000_000, 40, 30, 5, 1, 3)),
            BotLanguage.English);

        html.Should().StartWith("<h6>🔎 Discovery run (full)</h6>");
        html.Should().Contain("Crawl indexes: <b>12</b> (failed 1, left 3)<br>Records read: 1000000<br>");
        html.Should().Contain("Board tokens: 40, validated: 30<br>New boards in the registry: <b>5</b>");
    }

    [TestCase(1, "1 день")]
    [TestCase(3, "3 дня")]
    [TestCase(5, "5 дней")]
    [TestCase(11, "11 дней")]
    [TestCase(14, "14 дней")]
    [TestCase(21, "21 день")]
    [TestCase(22, "22 дня")]
    [TestCase(111, "111 дней")]
    public void Days_should_use_the_russian_plural_forms(int days, string expected)
    {
        StatsFormatter.Days(days, BotLanguage.Russian).Should().Be(expected);
    }

    [TestCase(1, "1 day")]
    [TestCase(21, "21 days")]
    public void Days_should_use_the_english_plural_forms(int days, string expected)
    {
        StatsFormatter.Days(days, BotLanguage.English).Should().Be(expected);
    }

    [TestCase("45", 365, true, 45)]
    [TestCase(" 7 ", 365, true, 7)]
    [TestCase("365", 365, true, 365)]
    [TestCase("366", 365, false, 0)]
    [TestCase("0", 365, false, 0)]
    [TestCase("-3", 365, false, 0)]
    [TestCase("1.5", 365, false, 0)]
    [TestCase("week", 365, false, 0)]
    public void TryParseDays_should_accept_only_a_whole_number_of_days_in_range(
        string input,
        int max,
        bool accepted,
        int expected)
    {
        var parsed = StatsScreen.TryParseDays(input, max, out var days);

        parsed.Should().Be(accepted);
        if (accepted)
            days.Should().Be(expected);
    }

    [Test]
    public void CallbackData_should_carry_the_period_of_a_statistics_button()
    {
        var raw = new CallbackData(CallbackAction.StatsOpen, 42, 30).ToString();

        raw.Should().Be("so:42:30");
        CallbackData.Parse(raw).Should().Be(new CallbackData(CallbackAction.StatsOpen, 42, 30));
        CallbackData.Parse("sc:42:7").Action.Should().Be(CallbackAction.StatsCustom);
    }

    [Test]
    public void Format_should_link_the_watched_companies_of_the_tops()
    {
        var watchlist = new Watchlist
        {
            Id = 1,
            Name = "Back & end",
            Entries =
            [
                new WatchlistEntry { Id = 11, VacancySourceId = "greenhouse", BoardId = "acme", CompanyName = "Acme" },
                new WatchlistEntry
                {
                    Id = 12, VacancySourceId = "greenhouse", BoardId = "delta", CompanyName = "Delta", Enabled = false
                }
            ]
        };

        var html = StatsFormatter.Format(
            Stats(), BotLanguage.English, digest: true, DeepLinks.Companies(watchlist, "pulse_bot"));

        html.Should().Contain("1. <a href=\"https://t.me/pulse_bot?start=c11\">Acme</a> — 7 new");
        html.Should().Contain("2. Delta — 4 new");
        html.Should().Contain("2. Beta — 1 events");
    }

    [Test]
    public void DeepLinks_should_read_back_a_company_payload_only()
    {
        DeepLinks.TryParseCompany("c42", out var entryId).Should().BeTrue();
        entryId.Should().Be(42);

        DeepLinks.TryParseCompany("c", out _).Should().BeFalse();
        DeepLinks.TryParseCompany("x42", out _).Should().BeFalse();
        DeepLinks.TryParseCompany(null, out _).Should().BeFalse();
    }

    [Test]
    public void Format_should_show_what_is_open_against_the_previous_digest()
    {
        var stats = Stats() with { OpenAtStart = new OpenCounts(40, 10), OpenAtEnd = new OpenCounts(47, 9) };

        var html = StatsFormatter.Format(stats, BotLanguage.English, digest: true, sincePrevious: true);

        html.Should().Contain("📊 What changed since the last digest · Back &amp; end");
        html.Should().Contain("📦 Open vacancies: <b>47</b> (were 40, +7)");
        html.Should().Contain("🏢 Companies with vacancies: <b>9</b> (were 10, −1)");
    }

    private static WatchlistStats Stats() =>
        new()
        {
            WatchlistId = 1,
            WatchlistName = "Back & end",
            Days = 3,
            From = To.AddDays(-3),
            To = To,
            Opened = 12,
            Closed = 5,
            NewCompanies = ["Acme", "Beta"],
            EmptiedCompanies = ["Gamma"],
            TopByActivity =
            [
                new CompanyActivity("Acme", new BoardActivity(5, 3, 2, 0.1)),
                new CompanyActivity("Beta", new BoardActivity(1, 0, 0, 0.1))
            ],
            TopByOpened = [new CompanyCount("Acme", 7), new CompanyCount("Delta", 4)]
        };
}
