using System;
using System.Collections.Generic;

namespace Taskboard;

public abstract class Entity<TKey> : IEquatable<Entity<TKey>>, IEqualityComparer<Entity<TKey>> where TKey : notnull
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

    // S3875: operator == requires the type to implement IEquatable<T> —
    // implemented above, so the operators are safe to define (B-10): they
    // must agree with Equals (id equality) instead of reference equality.
    public bool Equals(Entity<TKey>? other) => object.Equals(this, other);

    public bool Equals(Entity<TKey>? x, Entity<TKey>? y) => object.Equals(x, y);

    public int GetHashCode(Entity<TKey> obj) => obj.GetHashCode();

    public static bool operator ==(Entity<TKey>? left, Entity<TKey>? right) => object.Equals(left, right);

    public static bool operator !=(Entity<TKey>? left, Entity<TKey>? right) => !object.Equals(left, right);

    public override int GetHashCode() => Id is null ? 0 : EqualityComparer<TKey>.Default.GetHashCode(Id);
}
