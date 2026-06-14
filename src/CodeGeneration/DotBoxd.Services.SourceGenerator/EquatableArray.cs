// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// This file is a copy of the EquatableArray<T> type from the .NET Community Toolkit.
// The generator targets netstandard2.0, which doesn't have access to this type.
// Source: https://github.com/CommunityToolkit/dotnet/blob/main/src/CommunityToolkit.Mvvm.SourceGenerators/Helpers/EquatableArray%7BT%7D.cs

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace DotBoxd.Services.SourceGenerator;

/// <summary>
/// A wrapper for an <see cref="ImmutableArray{T}" /> that implements value equality.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _array;

    public ImmutableArray<T> Array => _array;

    public EquatableArray(ImmutableArray<T> array)
    {
        _array = array;
    }

    /// <summary>A canonical empty instance, backed by <see cref="ImmutableArray{T}.Empty"/>.</summary>
    public static EquatableArray<T> Empty { get; } = new(ImmutableArray<T>.Empty);

    public bool IsEmpty => _array.IsDefaultOrEmpty;

    public int Count => _array.IsDefault ? 0 : _array.Length;

    public T this[int index] => _array[index];

    public static implicit operator EquatableArray<T>(ImmutableArray<T> array) => new(array);

    public bool Equals(EquatableArray<T> other)
    {
        if (_array.IsDefault && other._array.IsDefault)
        {
            return true;
        }

        if (_array.IsDefault || other._array.IsDefault)
        {
            return false;
        }

        return _array.SequenceEqual(other._array);
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> array && Equals(array);

    public override int GetHashCode()
    {
        // Default and empty must hash the same so that two values that compare equal cannot
        // disagree on their hash, even though Equals also treats them as DIFFERENT from each
        // other (default vs empty is observable elsewhere via IsDefault). Returning 0 for
        // both is safe: it weakens distribution by exactly one bucket but never violates
        // the equals/hashcode contract.
        if (_array.IsDefaultOrEmpty)
        {
            return 0;
        }

        // Manual FNV-style aggregation to avoid taking a dependency on Microsoft.Bcl.HashCode.
        unchecked
        {
            int hash = 17;
            foreach (var item in _array)
            {
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }

    public ImmutableArray<T>.Enumerator GetEnumerator()
    {
        // Use a safe (potentially empty) array to avoid throwing on default.
        return (_array.IsDefault ? ImmutableArray<T>.Empty : _array).GetEnumerator();
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator() =>
        ((IEnumerable<T>)(_array.IsDefault ? ImmutableArray<T>.Empty : _array)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() =>
        ((IEnumerable)(_array.IsDefault ? ImmutableArray<T>.Empty : _array)).GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}

internal static class EquatableArrayExtensions
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> source)
        where T : IEquatable<T>
        => new(ImmutableArray.CreateRange(source));

    public static EquatableArray<T> ToEquatableArray<T>(this ImmutableArray<T> source)
        where T : IEquatable<T>
        => new(source);
}
