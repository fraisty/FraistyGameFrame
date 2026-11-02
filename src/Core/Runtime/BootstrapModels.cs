using System;
using System.Collections.Generic;

namespace FRFrame
{
    [Serializable]
    public sealed class BootstrapManifest
    {
        public string version;
        public string buildTime;
        public string yooAssetPackagesFile = BootstrapFileNames.YooAssetPackagesFile;
        public string uiBootstrapFile = BootstrapFileNames.UIBootstrapFile;
        public string hotfixManifestFile = BootstrapFileNames.HotfixManifestFile;
    }

    [Serializable]
    public sealed class UIBootstrapConfig
    {
        public string defaultUIConfigLocation = "Assets/GameRes/Common/Config/UI/UIConfigGroup_Default.asset";
        public string defaultPackageName;
    }

    [Serializable]
    public sealed class HotfixBootstrapManifest
    {
        public string version;
        public string buildTime;
        public List<HotfixFileRecord> aotFiles = new List<HotfixFileRecord>();
        public List<HotfixFileRecord> hotUpdateFiles = new List<HotfixFileRecord>();
    }

    [Serializable]
    public sealed class HotfixFileRecord
    {
        public string path;
        public long size;
        public string hash;
    }

    public static class BootstrapFileNames
    {
        public const string MainPackageDirectoryName = "MainPackage";
        public const string DirectoryName = "Bootstrap";
        public const string ManifestFile = "bootstrap_manifest.json";
        public const string YooAssetPackagesFile = "yooasset_packages.json";
        public const string UIBootstrapFile = "ui_bootstrap.json";
        public const string HotfixManifestFile = "hotfix_manifest.json";
    }
}
