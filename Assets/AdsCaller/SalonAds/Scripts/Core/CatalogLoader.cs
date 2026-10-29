using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

[Serializable]
public class RemoteGameEntry
{
    public string gameName;
    public string packageName;
    public string asin;

    public string bannerImages;

    public string interstitialImages;
    public string interstitialVideos;

    public string rewardedImages;
    public string rewardedVideos;

    public string bigbannerImage;
    public string popImages;

    public string Screenorientation;
}

[Serializable]
public class CrossPromoEntry
{
    public string packageName;
    public List<string> asins;
}

/// <summary>
/// Handles downloading and caching cross-promo game catalogs and their associated ad media.
/// </summary>
public class CatalogLoader : MonoBehaviour
{
    public static CatalogLoader Instance;

    #region Inspector Configurations

    [Header("Local Backup Catalog")]
    public GameCatalog localCatalog;

    [Header("Google Sheet JSON URLs")]
    public string sheet1CrossPromoURL;
    public string sheet2GameDataURL;

    [Header("Game Settings")]
    public bool isPortrait = true;

    [Header("Video Caching Settings")]
    public EnumsJar_Salon.VideoCacheMode videoCacheMode = EnumsJar_Salon.VideoCacheMode.LazyLoadOnShow;

    [Header("Video Priority Settings")]
    [Tooltip("If true, searches for remote Interstitial URLs first. If false, searches local VideoClips first.")]
    public bool prioritizeRemoteInterstitial = true;

    [Tooltip("If true, searches for remote Rewarded URLs first. If false, searches local VideoClips first.")]
    public bool prioritizeRemoteRewarded = true;

    [Tooltip("If remote data is missing videos, allow it to pull local VideoClips from the local backup catalog instead of failing.")]
    public bool allowLocalVideoFallback = true;

    #endregion

    #region Runtime Data

    public List<GameAdEntry> runtimeCatalog = new List<GameAdEntry>();
    private List<GameAdEntry> tempRemoteCatalog = new List<GameAdEntry>();

    [HideInInspector]
    public bool IsCatalogReady { get; private set; } = false;

    /// <summary>
    /// True after the remote catalog request has either been applied or definitively failed.
    /// IsCatalogReady can be true earlier because the local fallback is usable immediately.
    /// </summary>
    public bool IsRemoteLoadComplete { get; private set; } = false;

    #endregion

    #region Initialization

    private async void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Logger.Note("Initializing RemoteGameCatalogLoader with local backup catalog data.");

            runtimeCatalog = new List<GameAdEntry>(localCatalog.gameEntries);
            IsCatalogReady = true;

            if (AdsCallerSalon.Instance != null)
            {
                AdsCallerSalon.Instance.CheckAndInitialize();
            }

            // Trap the async void execution so silent crashes become visible
            try
            {
                await LoadCatalogAsync();
            }
            catch (Exception e)
            {
                Logger.Error($"[CatalogLoader] Fatal crash during LoadCatalogAsync: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                IsRemoteLoadComplete = true;
                Logger.Note("[CatalogLoader] Remote catalog loading finished.");
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #endregion

    #region Catalog Loading

    /// <summary>
    /// Fetches the mapping and game data sheets, filters for the current package, and triggers image caching.
    /// </summary>
    private async Awaitable LoadCatalogAsync()
    {
        using UnityWebRequest request1 = UnityWebRequest.Get(sheet1CrossPromoURL);
        request1.timeout = 30;
        await request1.SendWebRequest();

        Dictionary<string, List<string>> crossPromoMap = new Dictionary<string, List<string>>();

        if (request1.result == UnityWebRequest.Result.Success)
        {
            try
            {
                Logger.Note("Cross-promo mapping (Sheet 1) downloaded successfully.");
                crossPromoMap = ParseCrossPromoSheet(request1.downloadHandler.text);
            }
            catch (Exception e)
            {
                Logger.Warn("Failed to parse cross-promo mapping (Sheet 1): " + e.Message);
            }
        }
        else
        {
            Logger.Warn("Failed to download cross-promo mapping (Sheet 1). Proceeding with fallback.");
        }

        using UnityWebRequest request2 = UnityWebRequest.Get(sheet2GameDataURL);
        request2.timeout = 30;
        await request2.SendWebRequest();

        List<RemoteGameEntry> sheet2Entries = new List<RemoteGameEntry>();

        if (request2.result == UnityWebRequest.Result.Success)
        {
            try
            {
                Logger.Note("Remote game data (Sheet 2) downloaded successfully.");
                sheet2Entries = JsonConvert.DeserializeObject<List<RemoteGameEntry>>(request2.downloadHandler.text);
            }
            catch (Exception e)
            {
                Logger.Warn("Failed to parse remote game data (Sheet 2): " + e.Message);
            }
        }
        else
        {
            Logger.Warn("Failed to download remote game data (Sheet 2). Falling back to local catalog.");
            return;
        }

        Logger.Note("Filtering remote entries based on current package identifier.");

        if (sheet2Entries.Count > 0)
        {
            string currentPackage = Application.identifier;
            Logger.Note($"Current package identifier: {currentPackage}");

            if (crossPromoMap.TryGetValue(currentPackage, out var allowedAsins))
            {
                string targetOrientation = isPortrait ? "portrait" : "landscape";

                var filteredEntries = sheet2Entries
                    .Where(x => allowedAsins.Contains(x.asin) &&
                                x.Screenorientation != null &&
                                x.Screenorientation.ToLower() == targetOrientation)
                    .ToList();

                if (filteredEntries.Count == 0)
                {
                    Logger.Warn("No remote ads found matching the current orientation. Retaining local catalog.");
                    return;
                }

                tempRemoteCatalog.Clear();
                List<Awaitable> imageDownloadTasks = new List<Awaitable>();

                foreach (var remote in filteredEntries)
                {
                    GameAdEntry localMatch = new GameAdEntry();
                    bool hasLocalMatch = false;

                    if (localCatalog != null && localCatalog.gameEntries != null)
                    {
                        // Match by ASIN on Fire OS and by package name on the other stores.
                        bool isAmazonPlatform = AdsCallerSalon.DetectedStorePlatform ==
                                                EnumsJar_Salon.PlatformType.Amazon;

                        var match = localCatalog.gameEntries.FirstOrDefault(x =>
                            isAmazonPlatform
                                ? (!string.IsNullOrEmpty(x.asin) && x.asin == remote.asin)
                                : (!string.IsNullOrEmpty(x.gamePackageName) && x.gamePackageName == remote.packageName)
                        );

                        // Since match is a struct, validate it by checking its content fields instead of a null check
                        if (!string.IsNullOrEmpty(match.gamePackageName) || !string.IsNullOrEmpty(match.asin))
                        {
                            localMatch = match;
                            hasLocalMatch = true;
                        }
                    }

                    GameAdEntry entry = new GameAdEntry
                    {
                        gameName = remote.gameName,
                        gamePackageName = remote.packageName,
                        asin = remote.asin,
                        gameBannerImages = new List<Sprite>(),
                        gameInterstitialImages = new List<Sprite>(),
                        gameRewardedImages = new List<Sprite>(),
                        gameLargeBannerImages = new List<Sprite>(),
                        gamePopAdImages = new List<Sprite>(),
                        remoteInterstitialVideoURLs = new List<string>(),
                        remoteRewardedVideoURLs = new List<string>()
                    };

                    if (!string.IsNullOrEmpty(remote.interstitialVideos))
                    {
                        string[] iVids = remote.interstitialVideos.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var v in iVids)
                        {
                            // Convert the URL before adding it to the entry
                            string readyUrl = OutMediaUrlHelper.GetDirectDriveUrl(v.Trim());
                            entry.remoteInterstitialVideoURLs.Add(readyUrl);
                        }
                    }
                    else
                    {
                        Logger.Warn($"[CatalogLoader] Remote data missing Interstitial Video for {remote.gameName}.");
                        if (allowLocalVideoFallback && hasLocalMatch && localMatch.gameInterstitialVideos != null && localMatch.gameInterstitialVideos.Count > 0)
                        {
                            entry.gameInterstitialVideos = localMatch.gameInterstitialVideos;
                        }
                    }

                    // Rewarded Videos URL Checking
                    if (!string.IsNullOrEmpty(remote.rewardedVideos))
                    {
                        string[] rVids = remote.rewardedVideos.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var v in rVids)
                        {
                            // Convert the URL before adding it to the entry
                            string readyUrl = OutMediaUrlHelper.GetDirectDriveUrl(v.Trim());
                            entry.remoteRewardedVideoURLs.Add(readyUrl);
                        }
                    }
                    else
                    {
                        Logger.Warn($"[CatalogLoader] Remote data missing Rewarded Video for {remote.gameName}.");
                        if (allowLocalVideoFallback && hasLocalMatch && localMatch.gameRewardedVideos != null && localMatch.gameRewardedVideos.Length > 0)
                        {
                            entry.gameRewardedVideos = localMatch.gameRewardedVideos;
                        }
                    }

                    tempRemoteCatalog.Add(entry);

                    Logger.Note($"Evaluating images for game: {remote.gameName}");

                    // 1. Banner Fallback
                    if (!string.IsNullOrEmpty(remote.bannerImages))
                    {
                        imageDownloadTasks.Add(LoadImagesAsync(remote.bannerImages, entry.gameBannerImages, remote.gameName, "Banners"));
                    }
                    else if (hasLocalMatch && localMatch.gameBannerImages != null && localMatch.gameBannerImages.Count > 0)
                    {
                        Logger.Warn($"[CatalogLoader] Remote missing Banner for {remote.gameName}. Using local fallback.");
                        entry.gameBannerImages.AddRange(localMatch.gameBannerImages);
                    }

                    // 2. Interstitial Image Fallback
                    if (!string.IsNullOrEmpty(remote.interstitialImages))
                    {
                        imageDownloadTasks.Add(LoadImagesAsync(remote.interstitialImages, entry.gameInterstitialImages, remote.gameName, "Interstitials"));
                    }
                    else if (hasLocalMatch && localMatch.gameInterstitialImages != null && localMatch.gameInterstitialImages.Count > 0)
                    {
                        Logger.Warn($"[CatalogLoader] Remote missing Interstitial Image for {remote.gameName}. Using local fallback.");
                        entry.gameInterstitialImages.AddRange(localMatch.gameInterstitialImages);
                    }

                    // 3. Rewarded Image Fallback
                    if (!string.IsNullOrEmpty(remote.rewardedImages))
                    {
                        imageDownloadTasks.Add(LoadImagesAsync(remote.rewardedImages, entry.gameRewardedImages, remote.gameName, "Rewarded"));
                    }
                    else if (hasLocalMatch && localMatch.gameRewardedImages != null && localMatch.gameRewardedImages.Count > 0)
                    {
                        Logger.Warn($"[CatalogLoader] Remote missing Rewarded Image for {remote.gameName}. Using local fallback.");
                        entry.gameRewardedImages.AddRange(localMatch.gameRewardedImages);
                    }

                    // 4. Large Banner Fallback
                    if (!string.IsNullOrEmpty(remote.bigbannerImage))
                    {
                        imageDownloadTasks.Add(LoadImagesAsync(remote.bigbannerImage, entry.gameLargeBannerImages, remote.gameName, "BigBanners"));
                    }
                    else if (hasLocalMatch && localMatch.gameLargeBannerImages != null && localMatch.gameLargeBannerImages.Count > 0)
                    {
                        Logger.Warn($"[CatalogLoader] Remote missing Large Banner for {remote.gameName}. Using local fallback.");
                        entry.gameLargeBannerImages.AddRange(localMatch.gameLargeBannerImages);
                    }

                    // 5. Pop Ad Fallback
                    if (!string.IsNullOrEmpty(remote.popImages))
                    {
                        imageDownloadTasks.Add(LoadImagesAsync(remote.popImages, entry.gamePopAdImages, remote.gameName, "PopAds"));
                    }
                    else if (hasLocalMatch && localMatch.gamePopAdImages != null && localMatch.gamePopAdImages.Count > 0)
                    {
                        Logger.Warn($"[CatalogLoader] Remote missing Pop Ad for {remote.gameName}. Using local fallback.");
                        entry.gamePopAdImages.AddRange(localMatch.gamePopAdImages);
                    }
                }

                foreach (var task in imageDownloadTasks)
                {
                    await task;
                }

                // 1. Apply the catalog and initialize IMMEDIATELY after images are done
                runtimeCatalog = new List<GameAdEntry>(tempRemoteCatalog);

                CleanupOrphanedMedia();

                if (AdsCallerSalon.Instance != null)
                {
                    Logger.Note("Successfully applied remote catalog and re-initialized AdsCaller.");
                    AdsCallerSalon.Instance.SetRuntimeCatalog(runtimeCatalog);

                    if (AdsCallerSalon.Instance.config.canInitialize)
                        AdsCallerSalon.Instance.CheckAndInitialize();
                }

                // 2. Launch the video caching as an independent background task
                if (videoCacheMode == EnumsJar_Salon.VideoCacheMode.PreloadOnLaunch)
                {
                    // The underscore discards the awaitable, firing it off to run in the background
                    _ = PreloadVideosInBackgroundAsync(tempRemoteCatalog);
                }
            }
            else
            {
                Logger.Warn($"No cross-promo mapping found for this package: {currentPackage}. Retaining local catalog.");
            }
        }
    }

    /// <summary>
    /// Downloads videos sequentially in the background without blocking the main thread or catalog initialization.
    /// </summary>
    private async Awaitable PreloadVideosInBackgroundAsync(List<GameAdEntry> backgroundCatalog)
    {
        Logger.Note("Starting background video preloading...");

        foreach (var entry in backgroundCatalog)
        {
            if (entry.remoteInterstitialVideoURLs != null)
            {
                foreach (var url in entry.remoteInterstitialVideoURLs)
                {
                    await VideoCacher.GetOrDownloadVideoAsync(url, entry.gamePackageName, "inter");
                }
            }

            if (entry.remoteRewardedVideoURLs != null)
            {
                foreach (var url in entry.remoteRewardedVideoURLs)
                {
                    await VideoCacher.GetOrDownloadVideoAsync(url, entry.gamePackageName, "rewarded");
                }
            }
        }

        Logger.Note("Background video preloading complete.");
    }

    private Dictionary<string, List<string>> ParseCrossPromoSheet(string json)
    {
        List<Dictionary<string, string>> rawList = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(json);
        Dictionary<string, List<string>> map = new Dictionary<string, List<string>>();

        foreach (var row in rawList)
        {
            foreach (var kv in row)
            {
                string pkg = kv.Key;
                string asinsRaw = kv.Value;
                if (!string.IsNullOrEmpty(pkg) && !string.IsNullOrEmpty(asinsRaw))
                {
                    var asins = asinsRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(s => s.Trim())
                                        .ToList();

                    if (!map.ContainsKey(pkg))
                        map.Add(pkg, asins);
                }
            }
        }

        return map;
    }

    #endregion

    #region Image Loading & Caching

    /// <summary>
    /// Downloads images from the provided URLs. In the Editor, caches them locally for future use.
    /// Ensures files are saved with valid extensions and unique names to prevent overwriting.
    /// </summary>
    /// <summary>
    /// Downloads images from URLs if they don't exist locally. Caches them to device storage 
    /// at runtime to prevent redundant downloads on future sessions.
    /// </summary>
    private async Awaitable LoadImagesAsync(string urls, List<Sprite> targetList, string gameName, string adForm)
    {
        if (string.IsNullOrEmpty(urls)) return;

        string[] split = urls.Split(',');

        for (int i = 0; i < split.Length; i++)
        {
            string url = split[i].Trim();
            if (string.IsNullOrEmpty(url)) continue;

            string fileName = $"Image_{i}.png";
            try
            {
                Uri uri = new Uri(url);
                string extractedName = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrEmpty(extractedName) && Path.HasExtension(extractedName))
                {
                    fileName = $"{Path.GetFileNameWithoutExtension(extractedName)}_{i}{Path.GetExtension(extractedName)}";
                }
            }
            catch { }

            // 1. Create a safe runtime path on the device's persistent data path
            string safeGameName = string.Join("_", gameName.Split(Path.GetInvalidFileNameChars()));
            string dirPath = Path.Combine(Application.persistentDataPath, "AdCache", safeGameName, adForm);
            string filePath = Path.Combine(dirPath, fileName);

            // 2. CHECK LOCAL STORAGE FIRST (Differential Update Logic)
            if (File.Exists(filePath))
            {
                // File exists locally! Load it directly from the device to save bandwidth.
                string localUri = new Uri(filePath).AbsoluteUri;
                using (UnityWebRequest localRequest = UnityWebRequestTexture.GetTexture(localUri))
                {
                    await localRequest.SendWebRequest();
                    if (localRequest.result == UnityWebRequest.Result.Success)
                    {
                        Texture2D tex = DownloadHandlerTexture.GetContent(localRequest);
                        if (tex != null)
                        {
                            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                            targetList.Add(sprite);
                            continue; // Skip to the next image URL without hitting the internet
                        }
                    }
                }
                // If local load fails for some reason (corruption), it will naturally fall through and re-download.
            }

            // 3. FETCH FROM INTERNET (Only happens if file is missing)
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                await request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    if (texture != null)
                    {
                        // 4. CACHE TO LOCAL STORAGE FOR NEXT SESSION
                        try
                        {
                            if (!Directory.Exists(dirPath))
                                Directory.CreateDirectory(dirPath);

                            File.WriteAllBytes(filePath, texture.EncodeToPNG());
                        }
                        catch (Exception e)
                        {
                            Logger.Warn($"Failed to save local texture cache to disk: {e.Message}");
                        }

                        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                        targetList.Add(sprite);
                    }
                }
                else
                {
                    Logger.Warn($"Failed to download sprite from URL: {url}");
                }
            }
        }
    }

    #endregion

    #region Utility Method

    /// <summary>
    /// Scans the local device storage and deletes cached images and videos for games 
    /// that are no longer in the active remote catalog or the default local catalog.
    /// </summary>
    private void CleanupOrphanedMedia()
    {
        try
        {
            // 1. Build a HashSet of all SAFE game identifiers (Remote + Local)
            // HashSets are extremely fast for "Contains" lookups.
            HashSet<string> safeGameIds = new HashSet<string>();

            // Add live runtime (remote) games to the safe list
            if (runtimeCatalog != null)
            {
                foreach (var entry in runtimeCatalog)
                {
                    // For Images (uses Game Name)
                    if (!string.IsNullOrEmpty(entry.gameName))
                        safeGameIds.Add(string.Join("_", entry.gameName.Split(Path.GetInvalidFileNameChars())));

                    // For Videos (uses Package Name)
                    if (!string.IsNullOrEmpty(entry.gamePackageName))
                        safeGameIds.Add(string.Join("_", entry.gamePackageName.Split(Path.GetInvalidFileNameChars())).Replace(" ", ""));
                }
            }

            // Add local (default) games to ensure they are NEVER deleted
            if (localCatalog != null && localCatalog.gameEntries != null)
            {
                foreach (var entry in localCatalog.gameEntries)
                {
                    if (!string.IsNullOrEmpty(entry.gameName))
                        safeGameIds.Add(string.Join("_", entry.gameName.Split(Path.GetInvalidFileNameChars())));

                    if (!string.IsNullOrEmpty(entry.gamePackageName))
                        safeGameIds.Add(string.Join("_", entry.gamePackageName.Split(Path.GetInvalidFileNameChars())).Replace(" ", ""));
                }
            }

            // 2. CLEANUP IMAGES (Stored in the "AdCache" folder)
            string adCachePath = Path.Combine(Application.persistentDataPath, "AdCache");
            if (Directory.Exists(adCachePath))
            {
                string[] cachedGameFolders = Directory.GetDirectories(adCachePath);
                foreach (string folderPath in cachedGameFolders)
                {
                    string folderName = new DirectoryInfo(folderPath).Name;

                    // If the folder name isn't in our active Google Sheet OR our Local Backup...
                    if (!safeGameIds.Contains(folderName))
                    {
                        Logger.Note($"[Garbage Collection] Deleting orphaned image folder: {folderName}");
                        Directory.Delete(folderPath, true); // true = recursive delete (deletes all files inside)
                    }
                }
            }

            // 3. CLEANUP VIDEOS (Stored in persistentDataPath directly)
            // VideoCacher saves videos as {PackageName}_{adType}_{UrlHash}.mp4
            string[] allMp4Files = Directory.GetFiles(Application.persistentDataPath, "*.mp4");
            foreach (string filePath in allMp4Files)
            {
                string fileName = Path.GetFileName(filePath);
                bool isSafe = false;

                foreach (string safeId in safeGameIds)
                {
                    // The separator prevents package "com.game" from accidentally matching "com.game.two".
                    if (fileName.StartsWith(safeId + "_", StringComparison.Ordinal))
                    {
                        isSafe = true;
                        break;
                    }
                }

                if (!isSafe)
                {
                    Logger.Note($"[Garbage Collection] Deleting orphaned video: {fileName}");
                    File.Delete(filePath);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Warn($"[Garbage Collection] Error during cleanup: {e.Message}");
        }
    }
    #endregion
}

#region URL Formatting

/// <summary>
/// Utility for parsing and formatting remote media URLs for the ad system.
/// </summary>
public static class OutMediaUrlHelper
{
    /// <summary>
    /// Converts a standard Google Drive share link into a direct streamable URL.
    /// </summary>
    public static string GetDirectDriveUrl(string originalUrl)
    {
        if (string.IsNullOrWhiteSpace(originalUrl)) return originalUrl;

        string normalizedUrl = originalUrl.Trim();
        if (normalizedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            normalizedUrl = "https://" + normalizedUrl.Substring("http://".Length);
        }

        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri uri) ||
            (!string.Equals(uri.Host, "drive.google.com", StringComparison.OrdinalIgnoreCase) &&
             !uri.Host.EndsWith(".drive.google.com", StringComparison.OrdinalIgnoreCase)))
        {
            return normalizedUrl;
        }

        string fileId = null;
        string[] pathParts = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < pathParts.Length - 1; i++)
        {
            if (string.Equals(pathParts[i], "d", StringComparison.OrdinalIgnoreCase))
            {
                fileId = pathParts[i + 1];
                break;
            }
        }

        if (string.IsNullOrEmpty(fileId))
        {
            string query = uri.Query.TrimStart('?');
            foreach (string pair in query.Split('&'))
            {
                string[] keyValue = pair.Split(new[] { '=' }, 2);
                if (keyValue.Length == 2 && string.Equals(keyValue[0], "id", StringComparison.OrdinalIgnoreCase))
                {
                    fileId = Uri.UnescapeDataString(keyValue[1]);
                    break;
                }
            }
        }

        return string.IsNullOrWhiteSpace(fileId)
            ? normalizedUrl
            : $"https://drive.google.com/uc?export=download&id={Uri.EscapeDataString(fileId)}";
    }
}

#endregion
