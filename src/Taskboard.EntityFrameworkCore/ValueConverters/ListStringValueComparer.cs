using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Taskboard.EntityFrameworkCore.ValueConverters;

/// <summary>
/// Comparer for <see cref="IReadOnlyList{T}"/> of <see cref="string"/> properties
/// persisted through <see cref="ReadOnlyListStringJsonValueConverter"/>.
/// Required by EF Core so collection values are compared by content instead of
/// reference — otherwise mutations are not detected and the model emits the
/// "value converter without value comparer" warning.
/// </summary>
public sealed class ListStringValueComparer : ValueComparer<IReadOnlyList<string>>
{
    public ListStringValueComparer()
        : base(
            (left, right) => SequenceEquals(left, right),
            list => ComputeHash(list),
            // null! é intencional: o snapshot de uma coleção nula precisa ser
            // null — [] faria DetectChanges marcar a entidade como modified.
            list => list == null ? null! : list.ToList())
    {
    }

    private static int ComputeHash(IReadOnlyList<string>? list) =>
        list == null ? 0 : list.Aggregate(17, (hash, item) => hash * 31 + (item?.GetHashCode() ?? 0));

    private static bool SequenceEquals(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        ReferenceEquals(left, right) || (left is not null && right is not null && left.SequenceEqual(right));
}
