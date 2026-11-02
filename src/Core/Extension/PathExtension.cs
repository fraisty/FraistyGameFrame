using System.IO;
using UnityEngine;
namespace FRFrame
{
    public static class PathExtension
    {
        /// <summary>
        /// 额外资源包根目录名称。
        /// </summary>
        public static readonly string PackagesRootPathName = "yooPackages";
        
        public static string GetPersistentDataPath()
        {
        #if UNITY_EDITOR
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PersistentData");
        #endif
            return Application.persistentDataPath;
        }

        public static string GetPackagesRootPath()
        {
            return Path.Combine(GetPersistentDataPath(), PackagesRootPathName);
        }

        public static string GetMainPackageRootPath()
        {
            return Path.Combine(GetPersistentDataPath(), BootstrapFileNames.MainPackageDirectoryName);
        }

        public static string GetMainPackageHotFixRootPath()
        {
            return Path.Combine(GetMainPackageRootPath(), "HotFix");
        }

        public static string GetMainPackageBootstrapRootPath()
        {
            return Path.Combine(GetMainPackageRootPath(), BootstrapFileNames.DirectoryName);
        }

        public static string GetPackageInstallRootPath(string packageName)
        {
            return Path.Combine(GetPackagesRootPath(), packageName);
        }

        public static string GetPackagesExtensionPath(string packageName)
        {
            return Path.Combine(GetPackageInstallRootPath(packageName), "YooAsset");
        }
    }
}
