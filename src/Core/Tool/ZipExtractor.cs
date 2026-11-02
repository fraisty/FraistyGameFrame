using Cysharp.Threading.Tasks;
using System;
using System.IO;
using System.Threading;
using Unity.SharpZipLib.Zip;
using UnityEngine;

namespace FRFrame
{
    public readonly struct ZipExtractProgress
    {
        public readonly int Current;
        public readonly int Total;
        public readonly float Percent;
        public readonly string FileName;

        public ZipExtractProgress(int current, int total, float percent, string fileName)
        {
            Current = current;
            Total = total;
            Percent = Mathf.Clamp01(percent);
            FileName = fileName;
        }
    }

    public static class ZipExtractor
    {
        private const int BufferSize = 81920;

        public static async UniTask<bool> ExtractAsync(
            string zipPath,
            string extractPath,
            IProgress<ZipExtractProgress> onProgress = null,
            string password = null,
            bool overwrite = true,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
            {
                Debug.LogError($"Zip文件不存在: {zipPath}");
                return false;
            }

            if (string.IsNullOrEmpty(extractPath))
            {
                Debug.LogError("解压路径不能为空");
                return false;
            }

            Directory.CreateDirectory(extractPath);
            int totalFileCount = await UniTask.RunOnThreadPool(() => GetTotalFileCount(zipPath, password), cancellationToken: cancellationToken);
            if (totalFileCount <= 0)
            {
                onProgress?.Report(new ZipExtractProgress(0, 0, 1f, string.Empty));
                return true;
            }

            return await UniTask.RunOnThreadPool(
                () => Extract(zipPath, extractPath, totalFileCount, onProgress, password, overwrite, cancellationToken),
                cancellationToken: cancellationToken);
        }

        private static bool Extract(
            string zipPath,
            string extractPath,
            int totalFileCount,
            IProgress<ZipExtractProgress> onProgress,
            string password,
            bool overwrite,
            CancellationToken cancellationToken)
        {
            int processedCount = 0;
            string extractRoot = Path.GetFullPath(extractPath);
            using ZipFile zipFile = new ZipFile(zipPath);
            if (!string.IsNullOrEmpty(password))
            {
                zipFile.Password = password;
            }

            foreach (ZipEntry entry in zipFile)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                if (entry.IsDirectory)
                {
                    continue;
                }

                string entryName = entry.Name.Replace('\\', '/');
                string outputPath = Path.GetFullPath(Path.Combine(extractRoot, entryName));
                if (string.IsNullOrEmpty(entryName) || Path.IsPathRooted(entryName) || !outputPath.StartsWith(extractRoot, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogError($"Zip条目路径非法: {entry.Name}");
                    return false;
                }

                string directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(outputPath) || overwrite)
                {
                    ExtractEntry(zipFile, entry, outputPath);
                }

                processedCount++;
                onProgress?.Report(new ZipExtractProgress(processedCount, totalFileCount, (float)processedCount / totalFileCount, entryName));
            }

            return true;
        }

        private static int GetTotalFileCount(string zipPath, string password)
        {
            int count = 0;
            using ZipFile zipFile = new ZipFile(zipPath);
            if (!string.IsNullOrEmpty(password))
            {
                zipFile.Password = password;
            }

            foreach (ZipEntry entry in zipFile)
            {
                if (!entry.IsDirectory)
                {
                    count++;
                }
            }

            return count;
        }

        private static void ExtractEntry(ZipFile zipFile, ZipEntry entry, string outputPath)
        {
            using (Stream zipStream = zipFile.GetInputStream(entry))
            {
                using (FileStream fileStream = File.Create(outputPath))
                {
                    byte[] buffer = new byte[BufferSize];
                    int bytesRead;
                    while ((bytesRead = zipStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        fileStream.Write(buffer, 0, bytesRead);
                    }
                }
            }

            File.SetLastWriteTime(outputPath, entry.DateTime);
        }
    }
}
