using System;

namespace FRFrame
{
    [Serializable]
    public struct GamePackageVersion
    {
        public int major;
        public int full;
        public int patch;

        public GamePackageVersion(int major, int full, int patch)
        {
            this.major = Math.Max(0, major);
            this.full = Math.Max(0, full);
            this.patch = Math.Max(0, patch);
        }

        public static GamePackageVersion Default => new GamePackageVersion(1, 0, 0);

        public static GamePackageVersion Parse(string value)
        {
            if (!TryParse(value, out GamePackageVersion version))
            {
                throw new FormatException($"Invalid game package version: {value}");
            }

            return version;
        }

        public static bool TryParse(string value, out GamePackageVersion version)
        {
            version = Default;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string[] parts = value.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int full) || !int.TryParse(parts[2], out int patch))
            {
                return false;
            }

            if (major < 0 || full < 0 || patch < 0)
            {
                return false;
            }

            version = new GamePackageVersion(major, full, patch);
            return true;
        }

        public bool IsSameMajor(GamePackageVersion other)
        {
            return major == other.major;
        }

        public GamePackageVersion NextFull()
        {
            return new GamePackageVersion(major, full + 1, 0);
        }

        public GamePackageVersion NextPatch()
        {
            return new GamePackageVersion(major, full, patch + 1);
        }

        public override string ToString()
        {
            return $"{major}.{full}.{patch}";
        }
    }
}
