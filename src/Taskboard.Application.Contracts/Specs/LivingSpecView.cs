namespace Taskboard.Dtos;

/// <summary>
/// SPEC-20260922-living-specs-default-view RF-001 — partitions the /specs
/// catalog for the default view: every non-Done spec first (Deprecated stays
/// visible), then the latest <c>DoneCap</c> Done rows. Input order (spec Id
/// desc from the endpoint) is preserved inside each group.
/// </summary>
public static class LivingSpecView
{
    public const int DoneCap = 10;

    public static SpecPartition Partition(IReadOnlyList<LivingSpecDto> specs, int doneCap)
    {
        ArgumentNullException.ThrowIfNull(specs);

        var nonDone = new List<LivingSpecDto>(specs.Count);
        var done = new List<LivingSpecDto>();
        foreach (var spec in specs)
        {
            if (string.Equals(spec.Status, "Done", StringComparison.OrdinalIgnoreCase))
            {
                done.Add(spec);
            }
            else
            {
                nonDone.Add(spec);
            }
        }

        var shownDone = Math.Clamp(doneCap, 0, done.Count);
        var visible = new List<LivingSpecDto>(nonDone.Count + shownDone);
        visible.AddRange(nonDone);
        for (var i = 0; i < shownDone; i++)
        {
            visible.Add(done[i]);
        }

        return new SpecPartition(visible, done.Count - shownDone, done.Count);
    }
}

public sealed record SpecPartition(
    IReadOnlyList<LivingSpecDto> Visible,
    int HiddenDoneCount,
    int DoneCount);
