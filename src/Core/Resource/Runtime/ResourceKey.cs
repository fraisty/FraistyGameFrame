using System;

namespace FRFrame
{
    /// <summary>
    /// 资源唯一标识。
    /// 由 PackageName + Location 共同决定。
    /// 不再使用简单字符串拼接，避免多包冲突和可读性问题。
    /// </summary>
    public readonly struct ResourceKey : IEquatable<ResourceKey>
    {
        public readonly string PackageName;
        public readonly string Location;

        public ResourceKey(string packageName, string location)
        {
            PackageName = string.IsNullOrEmpty(packageName) ? string.Empty : packageName;
            Location = string.IsNullOrEmpty(location) ? string.Empty : location;
        }

        public bool Equals(ResourceKey other)
        {
            return PackageName == other.PackageName && Location == other.Location;
        }

        public override bool Equals(object obj)
        {
            return obj is ResourceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((PackageName != null ? PackageName.GetHashCode() : 0) * 397) ^
                    (Location != null ? Location.GetHashCode() : 0);
            }
        }

        public override string ToString()
        {
            return $"[{PackageName}]::{Location}";
        }
    }
}
