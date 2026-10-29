using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Handles downloading and local disk caching for remote video ads.
/// Tracks active downloads to prevent race conditions between background caching and foreground ad requests.
/// </summary>
public static class VideoCacher
{
    private const long MinimumValidVideoBytes = 1024;

    private sealed class DownloadState
    {
        public bool IsComplete;
        public string ResultPath;
    }

    // Unity Awaitable instances must not be awaited more than once, so callers wait on shared state
    // instead of sharing the same Awaitable object.
    private static readonly Dictionary<string, DownloadState> ActiveDownloads = new Dictionary<string, DownloadState>();

    public static async Awaitable<string> GetOrDownloadVideoAsync(string remoteUrl, string gameIdentifier, string adType)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl)) return null;

        string normalizedUrl = OutMediaUrlHelper.GetDirectDriveUrl(remoteUrl.Trim());
        string fileName = BuildCacheFileName(normalizedUrl, gameIdentifier, adType);
        string localFilePath = Path.Combine(Application.persistentDataPath, fileName);

        if (ActiveDownloads.TryGetValue(fileName, out DownloadState ongoingDownload))
        {
            Logger.Note($"[VideoCacher] {fileName} is already downloading. Waiting for it to finish...");
            while (!ongoingDownload.IsComplete)
            {
                await Awaitable.NextFrameAsync();
            }

            return ongoingDownload.ResultPath;
        }

        if (IsValidCacheFile(localFilePath))
        {
            return localFilePath;
        }

        DeleteFileIfPresent(localFilePath);

        DownloadState state = new DownloadState();
        ActiveDownloads[fileName] = state;

        try
        {
            state.ResultPath = await ExecuteDownloadAsync(normalizedUrl, localFilePath, fileName);
            return state.ResultPath;
        }
        catch (Exception exception)
        {
            Logger.Warn($"[VideoCacher] Failed to cache {fileName}: {exception.Message}");
            DeleteFileIfPresent(localFilePath);
            return null;
        }
        finally
        {
            state.IsComplete = true;
            ActiveDownloads.Remove(fileName);
        }
    }

    private static async Awaitable<string> ExecuteDownloadAsync(string remoteUrl, string localFilePath, string fileName)
    {
        string temporaryFilePath = localFilePath + ".download";
        DeleteFileIfPresent(temporaryFilePath);

        using (UnityWebRequest request = UnityWebRequest.Get(remoteUrl))
        {
            request.timeout = 60;
            request.downloadHandler = new DownloadHandlerFile(temporaryFilePath);

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Logger.Warn($"[VideoCacher] Video download failed: {request.error} | Response Code: {request.responseCode}");
                DeleteFileIfPresent(temporaryFilePath);
                return null;
            }

            string contentType = request.GetResponseHeader("Content-Type");
            if (!string.IsNullOrEmpty(contentType) && contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warn($"[VideoCacher] {fileName} returned an HTML page instead of a video. Check that the share link is public and direct-downloadable.");
                DeleteFileIfPresent(temporaryFilePath);
                return null;
            }

            if (!IsValidCacheFile(temporaryFilePath))
            {
                Logger.Warn($"[VideoCacher] Downloaded file is empty or incomplete: {fileName}");
                DeleteFileIfPresent(temporaryFilePath);
                return null;
            }

            DeleteFileIfPresent(localFilePath);
            File.Move(temporaryFilePath, localFilePath);
            Logger.Note($"[VideoCacher] Successfully cached: {fileName}");
            return localFilePath;
        }
    }

    private static bool IsValidCacheFile(string filePath)
    {
        try
        {
            return File.Exists(filePath) && new FileInfo(filePath).Length >= MinimumValidVideoBytes;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void DeleteFileIfPresent(string filePath)
    {
        try
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
        catch (Exception exception)
        {
            Logger.Warn($"[VideoCacher] Could not remove cache file '{Path.GetFileName(filePath)}': {exception.Message}");
        }
    }

    private static string BuildCacheFileName(string remoteUrl, string gameIdentifier, string adType)
    {
        string safeId = MakeSafeFileComponent(gameIdentifier, "unknown_game");
        string safeAdType = MakeSafeFileComponent(adType, "video");

        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(remoteUrl));
            string shortHash = BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant();
            return $"{safeId}_{safeAdType}_{shortHash}.mp4";
        }
    }

    private static string MakeSafeFileComponent(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        string safeValue = string.Join("_", value.Split(Path.GetInvalidFileNameChars())).Replace(" ", string.Empty);
        return string.IsNullOrEmpty(safeValue) ? fallback : safeValue;
    }
}
