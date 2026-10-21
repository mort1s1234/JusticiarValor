using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace JusticiarValor
{
    // Identity comparison for engine objects used as dictionary keys, without going through UnityEngine.Object's
    // Equals and GetHashCode (the Unity compatibility layer's on RimWorld 1.7).
    public sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

        public bool Equals(T x, T y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
