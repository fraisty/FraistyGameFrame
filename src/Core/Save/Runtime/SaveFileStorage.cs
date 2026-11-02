using System;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace FRFrame
{
    internal sealed class SaveFileStorage
    {
        private const string RootFolderName = "FRFrameSave";
        private const string SlotMetaName = "SlotMeta";
        private readonly string _rootPath;
        private readonly ISaveSerializer _serializer;

        public SaveFileStorage(ISaveSerializer serializer)
        {
            _serializer = serializer;
            _rootPath = Path.Combine(Application.persistentDataPath, RootFolderName);
        }

        public string RootPath => _rootPath;

        public string GetPath(SaveKey key)
        {
            string fileName = Sanitize(key.DataName) + _serializer.FileExtension;
            switch (key.Domain)
            {
                case SaveDomain.GlobalSettings:
                    return Path.Combine(_rootPath, "GlobalSettings", fileName);
                case SaveDomain.User:
                    if (string.IsNullOrEmpty(key.UserId))
                        return Path.Combine(_rootPath, "Users", "Default", fileName);
                    if (string.IsNullOrEmpty(key.PackageName))
                        return Path.Combine(_rootPath, "Users", Sanitize(key.UserId), fileName);
                    return Path.Combine(_rootPath, "Users", Sanitize(key.UserId), "Packages", Sanitize(key.PackageName), fileName);
                case SaveDomain.Slot:
                    return Path.Combine(_rootPath, "Slots", Sanitize(key.UserId), Sanitize(key.PackageName), Sanitize(key.SlotId), fileName);
                case SaveDomain.Cache:
                    if (string.IsNullOrEmpty(key.PackageName))
                        return Path.Combine(_rootPath, "Cache", fileName);
                    return Path.Combine(_rootPath, "Cache", Sanitize(key.PackageName), fileName);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public string GetSlotDirectory(string packageName, string userId, string slotId)
        {
            return Path.Combine(_rootPath, "Slots", Sanitize(userId), Sanitize(packageName), Sanitize(slotId));
        }

        public string GetSlotRootDirectory(string packageName, string userId)
        {
            return Path.Combine(_rootPath, "Slots", Sanitize(userId), Sanitize(packageName));
        }

        public SaveKey CreateSlotMetaKey(string packageName, string userId, string slotId)
        {
            return new SaveKey(SaveDomain.Slot, SlotMetaName, packageName, userId, slotId);
        }

        public UniTask WriteAsync(SaveKey key, byte[] bytes, bool backup)
        {
            return UniTask.RunOnThreadPool(() => Write(key, bytes, backup));
        }

        public void Write(SaveKey key, byte[] bytes, bool backup)
        {
            string path = GetPath(key);
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string tempPath = path + ".tmp";
            string backupPath = path + ".bak";
            File.WriteAllBytes(tempPath, bytes);
            if (backup && File.Exists(path))
                File.Copy(path, backupPath, true);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        public async UniTask<(bool success, byte[] bytes, bool fromBackup, string error)> ReadAsync(SaveKey key, bool allowBackup)
        {
            string path = GetPath(key);
            string backupPath = path + ".bak";
            try
            {
                if (!File.Exists(path))
                    return (false, null, false, "File not found");
                byte[] bytes = await UniTask.RunOnThreadPool(() => File.ReadAllBytes(path));
                return (true, bytes, false, null);
            }
            catch (Exception e)
            {
                if (!allowBackup || !File.Exists(backupPath))
                    return (false, null, false, e.ToString());

                try
                {
                    byte[] bytes = await UniTask.RunOnThreadPool(() => File.ReadAllBytes(backupPath));
                    return (true, bytes, true, null);
                }
                catch (Exception backupException)
                {
                    return (false, null, false, backupException.ToString());
                }
            }
        }

        public UniTask<bool> ExistsAsync(SaveKey key)
        {
            return UniTask.RunOnThreadPool(() => File.Exists(GetPath(key)));
        }

        public UniTask<bool> DeleteAsync(SaveKey key)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                string path = GetPath(key);
                string backupPath = path + ".bak";
                bool deleted = false;
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted = true;
                }
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                return deleted;
            });
        }

        public UniTask DeleteDirectoryAsync(string path)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            });
        }

        public UniTask CopyDirectoryAsync(string source, string target)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                if (!Directory.Exists(source))
                    return;
                if (Directory.Exists(target))
                    Directory.Delete(target, true);
                CopyDirectory(source, target);
            });
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (string directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }

        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "Default";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value;
        }
    }
}
