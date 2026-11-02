using System;

namespace FRFrame
{
    public sealed class ReferencePoolSnapshot
    {
        public Type Type;
        public int CachedCount;
        public int MaxCachedCount;
    }
}
