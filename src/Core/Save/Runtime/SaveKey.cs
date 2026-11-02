using System;

namespace FRFrame
{
    public readonly struct SaveKey : IEquatable<SaveKey>
    {
        public readonly SaveDomain Domain;
        public readonly string PackageName;
        public readonly string UserId;
        public readonly string SlotId;
        public readonly string DataName;

        public SaveKey(SaveDomain domain, string dataName, string packageName = null, string userId = null, string slotId = null)
        {
            Domain = domain;
            PackageName = Normalize(packageName);
            UserId = Normalize(userId);
            SlotId = Normalize(slotId);
            DataName = Normalize(dataName);
        }

        public bool Equals(SaveKey other)
        {
            return Domain == other.Domain && PackageName == other.PackageName && UserId == other.UserId && SlotId == other.SlotId && DataName == other.DataName;
        }

        public override bool Equals(object obj)
        {
            return obj is SaveKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)Domain;
                hashCode = (hashCode * 397) ^ PackageName.GetHashCode();
                hashCode = (hashCode * 397) ^ UserId.GetHashCode();
                hashCode = (hashCode * 397) ^ SlotId.GetHashCode();
                hashCode = (hashCode * 397) ^ DataName.GetHashCode();
                return hashCode;
            }
        }

        public override string ToString()
        {
            return $"{Domain}/{PackageName}/{UserId}/{SlotId}/{DataName}";
        }

        public static bool operator ==(SaveKey left, SaveKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SaveKey left, SaveKey right)
        {
            return !left.Equals(right);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value;
        }
    }
}
