using FluentAssertions;
using JobsPulse.Core.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Core;

/// <summary>
/// A batch is cut back to whole delivery windows: a window split by the batch cap arrived as two messages under the
/// same «what happened between …» header.
/// </summary>
public sealed class DeliveryWindowTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private static readonly DateTimeOffset W1 = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset W2 = W1 + Window;

    [Test]
    public void TakeWholeWindows_should_take_everything_below_the_cap()
    {
        DateTimeOffset[] items = [W1, W1.AddMinutes(1), W2];

        DeliveryWindow.TakeWholeWindows(items, x => x, 5, Window).Should().Equal(items);
    }

    [Test]
    public void TakeWholeWindows_should_leave_a_window_the_cap_cuts_through_for_the_next_batch()
    {
        // Cap 3 cuts the second window after its first item - the whole second window waits.
        DateTimeOffset[] items = [W1, W1.AddMinutes(1), W2, W2.AddMinutes(1)];

        DeliveryWindow.TakeWholeWindows(items, x => x, 3, Window).Should().Equal(W1, W1.AddMinutes(1));
    }

    [Test]
    public void TakeWholeWindows_should_take_a_window_ending_exactly_at_the_cap()
    {
        DateTimeOffset[] items = [W1, W1.AddMinutes(1), W2];

        DeliveryWindow.TakeWholeWindows(items, x => x, 2, Window).Should().Equal(W1, W1.AddMinutes(1));
    }

    [Test]
    public void TakeWholeWindows_should_split_only_a_single_window_bigger_than_the_cap()
    {
        DateTimeOffset[] items = [W1, W1.AddMinutes(1), W1.AddMinutes(2)];

        DeliveryWindow.TakeWholeWindows(items, x => x, 2, Window).Should().Equal(W1, W1.AddMinutes(1));
    }

    [Test]
    public void Floor_should_align_to_fifteen_minute_windows()
    {
        DeliveryWindow.Floor(W1.AddMinutes(14).AddSeconds(59), Window).Should().Be(W1);
        DeliveryWindow.Floor(W2, Window).Should().Be(W2);
    }
}
