using System.Text;
using JobsPulse.Core.Helpers;

namespace JobsPulse.Core.Model.Infrastructure;

public sealed record FilterSpec
{
    public IReadOnlyList<string> TitleAnyOf { get; init; } = [];
    public IReadOnlyList<string> TitleNoneOf { get; init; } = [];
    public IReadOnlyList<string> LocationAnyOf { get; init; } = [];
    public IReadOnlyList<string> LocationNoneOf { get; init; } = [];
    public IReadOnlyList<string> DescriptionAnyOf { get; init; } = [];
    public IReadOnlyList<string> DescriptionNoneOf { get; init; } = [];

    public int? PostedWithinDays { get; init; }

    public FilterMatchMode MatchMode { get; init; } = FilterMatchMode.Substring;

    public static readonly FilterSpec MatchAll = new();

    public bool IsEmpty =>
        TitleAnyOf.Count == 0 && TitleNoneOf.Count == 0 &&
        LocationAnyOf.Count == 0 && LocationNoneOf.Count == 0 &&
        DescriptionAnyOf.Count == 0 && DescriptionNoneOf.Count == 0 &&
        PostedWithinDays is null;


    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.Append($"{MatchMode:g}, {nameof(PostedWithinDays)}: {PostedWithinDays}");

        sb.AppendList(nameof(TitleAnyOf), TitleAnyOf);
        sb.AppendList(nameof(TitleNoneOf), TitleNoneOf);
        sb.AppendList(nameof(LocationAnyOf), LocationAnyOf);
        sb.AppendList(nameof(LocationNoneOf), LocationNoneOf);
        sb.AppendList(nameof(DescriptionAnyOf), DescriptionAnyOf);
        sb.AppendList(nameof(DescriptionNoneOf), DescriptionNoneOf);

        return sb.ToString();
    }
}