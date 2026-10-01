using System;

namespace Taskboard;

public abstract class Entity<TKey> : IEquatable<Entity<TKey>> where TKey : notnull
{
    public TKey Id { get; protected set; } = default!;

    protected Entity()
    {
    }

    protected Entity(TKey id)
    {
        Id = id;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TKey> other)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Id is null || other.Id is null)
        {
            return false;
        }

        return EqualityComparer<TKey>.Default.Equals(Id, other.Id);
    }

    // S3875: operator == requires the type to implement IEquatable<T>.
    public bool Equals(Entity<TKey>? other) => object.Equals(this, other);

    public override int GetHashCode() => Id is null ? 0 : EqualityComparer<TKey>.Default.GetHashCode(Id);
}
