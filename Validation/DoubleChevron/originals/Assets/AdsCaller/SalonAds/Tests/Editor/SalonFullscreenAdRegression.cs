using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Batch play-mode checks against the shipped prefabs; does not open a real store.</summary>
[InitializeOnLoad]
public static class SalonFullscreenAdRegression
{
    private const string Pending = "SalonFullscreenAdRegression.Pending";
    private const string Prefabs = "Assets/AdsCaller/SalonAds/Prefabs/AdBodies/";
    private static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    private static readonly List<string> passed = new List<string>();
    private static double started;
    private static Sprite sprite;
    private static AdsConfiguration configuration;
    private static string output;

    static SalonFullscreenAdRegression()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            output = Path.GetFullPath("Validation/FullscreenAds");
            Directory.CreateDirectory(output);
            started = EditorApplication.timeSinceStartup;
            steps.Push(CheckAll());
            EditorApplication.update += Tick;
        };
    }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - started > 150) throw new Exception("Regression suite timed out.");
            EditorApplication.QueuePlayerLoopUpdate();
            while (steps.Count > 0)
            {
                var current = steps.Peek();
                if (!current.MoveNext()) { steps.Pop(); continue; }
                if (current.Current is IEnumerator nested) { steps.Push(nested); continue; }
                return;
            }
            Finish(null);
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void Finish(Exception exception)
    {
        EditorApplication.update -= Tick;
        Time.timeScale = 1f;
        AudioListener.volume = 1f;
        File.WriteAllText(Path.Combine(output, "results.txt"), string.Join("\n", passed) +
            (exception == null ? "\nPASS: All checks completed.\n" : "\nFAIL: " + exception));
        if (exception != null) Debug.LogException(exception);
        else Debug.Log("FULLSCREEN_AD_REGRESSION_PASSED: " + passed.Count + " checks");
        EditorApplication.Exit(exception == null ? 0 : 1);
    }

    private static IEnumerator CheckAll()
    {
        configuration = ScriptableObject.CreateInstance<AdsConfiguration>();
        Require(!configuration.interstitialShowStoreArrowBeforeClose && !configuration.rewardedShowStoreArrowBeforeClose, "New configuration defaults to direct close");
        sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);
        foreach (string orientation in new[] { "", "_landscape" })
        {
            foreach (bool arrow in new[] { false, true })
            {
                yield return Interstitial(orientation, arrow);
                yield return Rewarded(orientation, arrow);
            }
        }
        yield return ConfigurationSwitching();
        yield return FailureAndReuse();
        yield return PlaybackWatchdogs();
        yield return MissingControls();
        yield return Reel();
        Object.Destroy(sprite);
        Object.Destroy(configuration);
    }

    private static GameObject Load(string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + name + ".prefab");
        Require(prefab != null, "Prefab exists: " + name);
        return Object.Instantiate(prefab);
    }

    private static IEnumerator Interstitial(string orientation, bool arrow)
    {
        Time.timeScale = 0.65f;
        AudioListener.volume = 0.37f;
        var go = Load("Interstitial" + orientation);
        var ad = go.GetComponent<SalonInterstitialAd>();
        configuration.interstitialShowStoreArrowBeforeClose = arrow;
        ad.closeButtonDelay = 0.3f;
        go.SetActive(true);
        ad.SetupImage(sprite, configuration);
        ad.OnCloseButtonClicked();
        Require(go.activeSelf, "Interstitial cannot close during countdown");
        yield return WaitUntil(() => ad.closeButton.gameObject.activeSelf);
        var control = Get<SalonAdCloseControl>(ad, "_closeControl");
        Require(control.ShowingArrow == arrow, "Interstitial setting controls arrow state");
        if (arrow)
        {
            yield return Wait(0.5f);
            Capture(go, "interstitial-arrow" + orientation, orientation.Length > 0);
            ad.closeButton.onClick.Invoke();
            Require(go.activeSelf && !control.ShowingArrow && control.IsReady, "Interstitial arrow reveals close without dismissing");
            ad.closeButton.onClick.Invoke();
            Require(go.activeSelf, "Same-frame duplicate arrow click cannot close");
            yield return NextFrame();
        }
        ad.closeButton.onClick.Invoke();
        Require(!go.activeSelf, "Interstitial closes");
        Require(Mathf.Approximately(Time.timeScale, 0.65f) && Mathf.Approximately(AudioListener.volume, 0.37f), "Interstitial restores prior state");
        go.SetActive(true);
        ad.closeButtonDelay = 0f;
        ad.SetupImage(sprite, configuration);
        Require(control.ShowingArrow == arrow, "Interstitial reuse resets arrow");
        go.SetActive(false);
        Object.Destroy(go);
        Pass("Interstitial " + orientation + " arrow=" + arrow);
        yield return NextFrame();
    }

    private static IEnumerator Rewarded(string orientation, bool arrow)
    {
        Time.timeScale = 0.7f;
        AudioListener.volume = 0.42f;
        int success = 0, failure = 0;
        var go = Load("Rewarded" + orientation);
        var ad = go.GetComponent<OutSalonRewardedAd>();
        configuration.rewardedShowStoreArrowBeforeClose = arrow;
        ad.imageRewardDuration = 0.3f;
        go.SetActive(true);
        ad.SetupImageAd(sprite, () => success++, () => failure++, configuration);
        yield return WaitUntil(() => ad.closeButton.gameObject.activeSelf);
        Require(success == 0 && failure == 0, "Reward callback waits for close");
        var control = Get<SalonAdCloseControl>(ad, "_closeControl");
        Require(control.ShowingArrow == arrow, "Rewarded setting controls arrow");
        if (arrow)
        {
            ad.closeButton.onClick.Invoke();
            Require(go.activeSelf && !control.ShowingArrow && success == 0, "Rewarded arrow reveals close without granting twice");
            ad.closeButton.onClick.Invoke();
            Require(success == 0, "Duplicate arrow click does not grant reward");
            yield return NextFrame();
        }
        ad.closeButton.onClick.Invoke();
        ad.OnCloseClicked();
        Require(success == 1 && failure == 0 && !go.activeSelf, "Reward succeeds exactly once");
        Require(Mathf.Approximately(Time.timeScale, 0.7f) && Mathf.Approximately(AudioListener.volume, 0.42f), "Rewarded restores prior state");
        Object.Destroy(go);
        Pass("Rewarded " + orientation + " arrow=" + arrow);
        yield return NextFrame();
    }

    private static IEnumerator ConfigurationSwitching()
    {
        foreach (string orientation in new[] { "", "_landscape" })
        {
            var interGo = Load("Interstitial" + orientation);
            var rewardGo = Load("Rewarded" + orientation);
            var inter = interGo.GetComponent<SalonInterstitialAd>();
            var reward = rewardGo.GetComponent<OutSalonRewardedAd>();
            interGo.SetActive(false);
            rewardGo.SetActive(false);
            inter.closeButtonDelay = reward.imageRewardDuration = 0.1f;
            Require(new SerializedObject(inter).FindProperty("showStoreArrowBeforeClose") == null,
                "Interstitial prefab no longer owns arrow setting");
            Require(new SerializedObject(reward).FindProperty("showStoreArrowBeforeClose") == null,
                "Rewarded prefab no longer owns arrow setting");

            foreach (bool interArrow in new[] { false, true })
            foreach (bool rewardArrow in new[] { false, true })
            {
                configuration.interstitialShowStoreArrowBeforeClose = interArrow;
                configuration.rewardedShowStoreArrowBeforeClose = rewardArrow;
                interGo.SetActive(true);
                inter.SetupImage(sprite, configuration);
                // Edits during an active ad apply only to the next presentation.
                configuration.interstitialShowStoreArrowBeforeClose = !interArrow;
                yield return WaitUntil(() => inter.closeButton.gameObject.activeSelf);
                Require(Get<SalonAdCloseControl>(inter, "_closeControl").ShowingArrow == interArrow,
                    "Interstitial uses its own configuration snapshot");
                interGo.SetActive(false);

                rewardGo.SetActive(true);
                reward.SetupImageAd(sprite, null, null, configuration);
                configuration.rewardedShowStoreArrowBeforeClose = !rewardArrow;
                yield return WaitUntil(() => reward.closeButton.gameObject.activeSelf);
                Require(Get<SalonAdCloseControl>(reward, "_closeControl").ShowingArrow == rewardArrow,
                    "Rewarded uses its own configuration snapshot");
                rewardGo.SetActive(false);
            }
            // Legacy direct callers remain supported and cannot inherit an earlier true setting.
            interGo.SetActive(true);
            inter.SetupImage(sprite);
            yield return WaitUntil(() => inter.closeButton.gameObject.activeSelf);
            Require(!Get<SalonAdCloseControl>(inter, "_closeControl").ShowingArrow, "No configuration gives direct interstitial close");
            inter.OnCloseButtonClicked();
            rewardGo.SetActive(true);
            reward.SetupImageAd(sprite, null, null);
            yield return WaitUntil(() => reward.closeButton.gameObject.activeSelf);
            Require(!Get<SalonAdCloseControl>(reward, "_closeControl").ShowingArrow, "No configuration gives direct rewarded close");
            reward.OnCloseClicked();
            Object.Destroy(interGo);
            Object.Destroy(rewardGo);
            Pass("Configuration ownership, independent toggles, cached UI reuse, snapshot and legacy defaults " + orientation);
            yield return NextFrame();
        }
    }

    private static IEnumerator FailureAndReuse()
    {
        int success = 0, failure = 0;
        var go = Load("Rewarded");
        var ad = go.GetComponent<OutSalonRewardedAd>();
        configuration.rewardedShowStoreArrowBeforeClose = true;
        ad.imageRewardDuration = 10f;
        go.SetActive(true);
        ad.SetupImageAd(sprite, () => success++, () => failure++, configuration);
        Invoke(ad, "HandleVideoFailure", "Regression: injected failure during intro");
        var control = Get<SalonAdCloseControl>(ad, "_closeControl");
        Require(control.IsReady && !control.ShowingArrow && go.GetComponent<CanvasGroup>().alpha == 1f, "Failure enables visible close immediately");
        Invoke(ad, "GrantReward");
        ad.OnCloseClicked();
        Require(success == 0 && failure == 1, "Failure and late completion cannot grant reward");
        yield return Wait(0.8f);
        Require(!go.activeSelf, "Old intro cannot reactivate failed ad");
        go.SetActive(true);
        ad.imageRewardDuration = 0f;
        ad.SetupImageAd(sprite, () => success++, () => failure++, configuration);
        Require(control.ShowingArrow, "Failure state resets on reuse");
        ad.OnCloseClicked();
        yield return NextFrame();
        ad.OnCloseClicked();
        Require(success == 1 && failure == 1, "Reused rewarded ad succeeds");
        go.SetActive(true);
        ad.imageRewardDuration = 10f;
        ad.SetupImageAd(sprite, () => success++, () => failure++, configuration);
        go.SetActive(false);
        Require(failure == 2, "External dismissal completes pending request");
        float disabledAlpha = go.GetComponent<CanvasGroup>().alpha;
        yield return Wait(0.8f);
        Require(go.GetComponent<CanvasGroup>().alpha == disabledAlpha, "Disabled intro stops modifying UI");
        go.SetActive(true);
        ad.SetupImageAd(null, () => success++, () => failure++, configuration);
        Require(!go.activeSelf && failure == 3, "Missing image fails once and releases game state");
        Object.Destroy(go);

        var interGo = Load("Interstitial");
        var inter = interGo.GetComponent<SalonInterstitialAd>();
        configuration.interstitialShowStoreArrowBeforeClose = true;
        inter.closeButtonDelay = 0.2f;
        interGo.SetActive(true);
        inter.SetupImage(sprite, configuration);
        Invoke(inter, "HandleVideoFailure", "Regression: timer/failure race");
        yield return Wait(0.8f);
        var interControl = Get<SalonAdCloseControl>(inter, "_closeControl");
        Require(interControl.IsReady && !interControl.ShowingArrow && interGo.GetComponent<CanvasGroup>().alpha == 1f, "Old timer cannot replace failure close with arrow");
        inter.OnCloseButtonClicked();
        Require(!interGo.activeSelf, "Failed interstitial closes");
        Object.Destroy(interGo);
        Pass("Failure, late completion, timer race, external disable, missing image and reuse");
        yield return NextFrame();

        // Exercise the actual preparation watchdog without making any network request.
        go = Load("Rewarded");
        ad = go.GetComponent<OutSalonRewardedAd>();
        go.SetActive(true);
        ad.SetupImageAd(sprite, () => success++, () => failure++, configuration);
        ad.videoPreparationTimeout = 1f;
        if (Application.unityVersion.StartsWith("2022"))
            ad.StartCoroutine((IEnumerator)Invoke(ad, "PrepareAndTrackVideoRoutine"));
        else
            Invoke(ad, "PrepareAndTrackVideoRoutine", System.Threading.CancellationToken.None);
        yield return WaitUntil(() => ad.closeButton.gameObject.activeSelf, 3f);
        Require(!Get<SalonAdCloseControl>(ad, "_closeControl").ShowingArrow, "Preparation timeout bypasses arrow");
        ad.OnCloseClicked();
        Object.Destroy(go);
        Pass("Video preparation timeout");
    }

    private static IEnumerator PlaybackWatchdogs()
    {
        foreach (bool unknown in new[] { false, true })
        {
            int failures = 0;
            var go = Load("Rewarded");
            var ad = go.GetComponent<OutSalonRewardedAd>();
            configuration.rewardedShowStoreArrowBeforeClose = true;
            ad.imageRewardDuration = 10f;
            ad.videoStallTimeout = unknown ? 10f : 1f;
            ad.unknownVideoDurationTimeout = unknown ? 1f : 10f;
            go.SetActive(true);
            ad.SetupImageAd(sprite, () => { throw new Exception("Stalled video must not earn reward"); }, () => failures++, configuration);
            Invoke(ad, Application.unityVersion.StartsWith("2022") ? "StopAdRoutine" : "ResetCancellationToken");
            if (Application.unityVersion.StartsWith("2022"))
                ad.StartCoroutine((IEnumerator)Invoke(ad, "VideoTimerUpdateRoutine"));
            else
                Invoke(ad, "VideoTimerUpdateRoutine", System.Threading.CancellationToken.None);
            yield return WaitUntil(() => ad.closeButton.gameObject.activeSelf, 3f);
            Require(!Get<SalonAdCloseControl>(ad, "_closeControl").ShowingArrow, "Playback watchdog bypasses store arrow");
            ad.OnCloseClicked();
            Require(failures == 1 && !go.activeSelf, "Playback watchdog releases ad with one failure");
            Object.Destroy(go);
            Pass(unknown ? "Unknown-duration video watchdog" : "Video playback stall watchdog");
            yield return NextFrame();
        }
    }

    private static void Capture(GameObject root, string name, bool landscape = false)
    {
        var cameraObject = new GameObject("Validation camera", typeof(Camera));
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
        camera.orthographic = true;
        var target = new RenderTexture(landscape ? 900 : 600, landscape ? 600 : 900, 24);
        camera.targetTexture = target;
        var canvas = root.GetComponent<Canvas>();
        RenderMode oldMode = canvas.renderMode;
        Camera oldCamera = canvas.worldCamera;
        float oldDistance = canvas.planeDistance;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(output, name + ".png"), texture.EncodeToPNG());
        RenderTexture.active = previous;
        canvas.renderMode = oldMode;
        canvas.worldCamera = oldCamera;
        canvas.planeDistance = oldDistance;
        camera.targetTexture = null;
        target.Release();
        Object.Destroy(target);
        Object.Destroy(texture);
        Object.Destroy(cameraObject);
    }

    private static IEnumerator MissingControls()
    {
        var owner = new GameObject("Missing close reference", typeof(RectTransform));
        var control = new SalonAdCloseControl(owner.transform, null);
        control.Show(true);
        int clicked = 0;
        Require(control.HandleArrowClick(() => { clicked++; Require(!control.ShowingArrow, "Close is revealed before store callback"); throw new Exception("Regression: simulated store launch failure"); }), "Arrow consumes click");
        Require(clicked == 1 && control.IsReady && !control.ShowingArrow && control.Button.gameObject.activeSelf, "Throwing store callback leaves fallback close available");
        yield return NextFrame();
        Require(!control.HandleArrowClick(null), "Next click can dismiss after store failure");
        Object.Destroy(owner);
        Pass("Missing close reference and failed store launch");
    }

    private static IEnumerator Reel()
    {
        var go = Load("Rewarded");
        var ad = go.GetComponent<OutSalonRewardedAd>();
        go.SetActive(true);
        ad.imageRewardDuration = 10f;
        ad.SetupImageAd(sprite, null, null);
        Invoke(ad, Application.unityVersion.StartsWith("2022") ? "StopAdRoutine" : "ResetCancellationToken");
        var reel = ad.timerText.GetComponent<SalonAdTimerReel>();
        reel.SetCountdown(12, "Reward in", "s");
        Canvas.ForceUpdateCanvases();
        reel.SetCountdown(11, "Reward in", "s");
        yield return Wait(0.08f);
        var first = ad.timerText.transform.Find("Timer reel/Countdown/Digit 0/Current").GetComponent<RectTransform>();
        var second = ad.timerText.transform.Find("Timer reel/Countdown/Digit 1/Current").GetComponent<RectTransform>();
        Require(first.anchoredPosition.y == 0f && second.anchoredPosition.y > 0f, "Only changed digit rolls at timeScale zero");
        reel.SetCountdown(9, "Reward in", "s");
        yield return Wait(0.3f);
        Require(first.anchoredPosition == Vector2.zero && second.anchoredPosition == Vector2.zero, "Reels settle after interrupted multi-digit change");
        Require(ad.timerText.text == "Reward in 9s", "Timer source tracks latest value");
        // Snapshot the actual prefab and timer layout for visual inspection.
        Capture(go, "rewarded-timer");
        yield return Wait(0.3f);
        Invoke(ad, "GrantReward");
        yield return Wait(0.6f);
        Require(ad.timerText.text == "Reward unlocked", "Completion rolls into unlocked message");
        Capture(go, "rewarded-unlocked");
        yield return Wait(0.3f);
        go.SetActive(false);
        Object.Destroy(go);
        Pass("Unscaled digit reels, rapid changes and completion message");
    }

    private static IEnumerator Wait(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator NextFrame()
    {
        int frame = Time.frameCount;
        while (Time.frameCount == frame) yield return null;
    }

    private static IEnumerator WaitUntil(Func<bool> condition, float timeout = 3f)
    {
        float until = Time.realtimeSinceStartup + timeout;
        while (!condition())
        {
            Require(Time.realtimeSinceStartup < until, "Timed out waiting for an ad control");
            yield return null;
        }
    }

    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    private static object Invoke(object target, string method, params object[] arguments) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, arguments);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Pass(string message) { passed.Add("PASS: " + message); Debug.Log("PASS: " + message); }
}
