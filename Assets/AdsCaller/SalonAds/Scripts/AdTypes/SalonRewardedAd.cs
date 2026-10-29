using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Handles rewarded ad displays (Image/Video) using modern Awaitable async patterns.
/// Safely manages global engine states and UI animations.
/// </summary>
public class OutSalonRewardedAd : MonoBehaviour
{
    #region Variables

    [Header("Media Renderers")]
    public Image imageDisplay;
    public RawImage videoDisplay;
    public VideoPlayer videoPlayer;

    [Header("UI Controls")]
    public Button mainAdButton;
    public Button closeButton;

    // Snapshot of the provider configuration for this presentation; not a prefab setting.
    private bool _showStoreArrowBeforeClose;

    private SalonAdCloseControl _closeControl;
    private SalonAdTimerReel _timerReel;
    public RectTransform textBody;
    public Text timerText;

    [Header("Timer Settings")]
    [Tooltip("Duration required to watch the image ad (in seconds) before granting reward.")]
    public float imageRewardDuration = 5f;

    [Tooltip("Maximum time allowed for a video to prepare before the ad fails.")]
    public float videoPreparationTimeout = 15f;

    [Tooltip("Maximum time a prepared video may make no playback progress before the ad fails.")]
    public float videoStallTimeout = 15f;

    [Tooltip("Safety timeout for streams with no reported duration or completion event. Timeout fails the ad and enables Close.")]
    public float unknownVideoDurationTimeout = 120f;

    [Header("Text Body Animation")]
    public float minWidth = 120f;
    public float maxWidth = 550f;
    public float minHeight = 120f;
    public float maxHeight = 250f;
    public float animationDuration = 0.5f;

    private Action onSuccess;
    private Action onFail;

    private bool rewardEarned = false;
    private float currentTimer;

    // Explicit cancellation tokens to handle script reuse and state changes safely
    private CancellationTokenSource adRoutineCts;
    private CancellationTokenSource introCts;

    // Animation tracking
    private CanvasGroup _canvasGroup;
    private Vector2 _originalImagePos;
    private Vector2 _originalVideoPos;
    private float _previousTimeScale;
    private float _previousAudioVolume;
    private bool _globalStatesCaptured;
    private bool _videoFailed;

    #endregion

    #region Initialization & Setup

    private void Awake()
    {
        _closeControl = new SalonAdCloseControl(transform, closeButton);
        closeButton = _closeControl.Button;
        _timerReel = SalonAdTimerReel.Attach(timerText);
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);

        if (mainAdButton != null)
            mainAdButton.onClick.AddListener(OnRewardedAdClicked);

        // Setup CanvasGroup for fading
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Cache original positions for the slide-up animation
        if (imageDisplay != null) _originalImagePos = imageDisplay.rectTransform.anchoredPosition;
        if (videoDisplay != null) _originalVideoPos = videoDisplay.rectTransform.anchoredPosition;
    }

    private void OnEnable()
    {
        _previousTimeScale = Time.timeScale;
        _previousAudioVolume = AudioListener.volume;
        _globalStatesCaptured = true;
    }

    private void Update()
    {
        if (WasCancelPressed())
        {
            if (CanPress(closeButton)) closeButton.onClick.Invoke();
            return;
        }

        if (WasConfirmPressed())
        {
            Button target = _closeControl.ShowingArrow ? closeButton : mainAdButton;
            if (CanPress(target)) target.onClick.Invoke();
        }
    }

    private static bool WasConfirmPressed() => AdsUiInput.WasConfirmPressed();

    private static bool WasCancelPressed() => AdsUiInput.WasCancelPressed();

    private static bool CanPress(Button target)
    {
        return target != null && target.isActiveAndEnabled && target.interactable;
    }

    /// <summary>
    /// Configures and starts an image-based rewarded ad.
    /// </summary>
    public void SetupImageAd(Sprite sprite, Action successCallback, Action failCallback)
    {
        SetupImageAd(sprite, successCallback, failCallback, null);
    }

    public void SetupImageAd(Sprite sprite, Action successCallback, Action failCallback, AdsConfiguration configuration)
    {
        _showStoreArrowBeforeClose = configuration != null && configuration.rewardedShowStoreArrowBeforeClose;
        // 1. Hard safety check to prevent blank screens
        if (sprite == null || imageDisplay == null)
        {
            Debug.LogError("[OutSalonRewardedAd] SetupImageAd called with a null sprite! Check your catalog. Aborting and firing failCallback.");
            onSuccess = null;
            onFail = null;
            gameObject.SetActive(false);
            failCallback?.Invoke();
            return;
        }

        ResetAdState(successCallback, failCallback);
        UnsubscribeVideoEvents();
        if (videoPlayer != null) videoPlayer.Stop();

        if (imageDisplay != null) imageDisplay.gameObject.SetActive(true);
        if (videoDisplay != null) videoDisplay.gameObject.SetActive(false);
        if (videoPlayer != null) videoPlayer.gameObject.SetActive(false);

        if (imageDisplay != null) imageDisplay.sprite = sprite;

        StartIntroAnimation();
        StartAdRoutine(ImageTimerRoutine);
    }

    /// <summary>
    /// Configures and starts a video-based rewarded ad via URL or AudioClip.
    /// </summary>
    public bool SetupVideoAd(VideoClip clip, Action successCallback, Action failCallback, string url = null)
    {
        return SetupVideoAd(clip, successCallback, failCallback, url, null);
    }

    public bool SetupVideoAd(VideoClip clip, Action successCallback, Action failCallback, string url, AdsConfiguration configuration)
    {
        _showStoreArrowBeforeClose = configuration != null && configuration.rewardedShowStoreArrowBeforeClose;
        if (videoPlayer == null || videoDisplay == null || (clip == null && string.IsNullOrWhiteSpace(url)))
        {
            Debug.LogError("[OutSalonRewardedAd] Cannot show video: the player or video source is missing.");
            onSuccess = null;
            onFail = null;
            gameObject.SetActive(false);
            failCallback?.Invoke();
            return false;
        }

        ResetAdState(successCallback, failCallback);
        UnsubscribeVideoEvents();
        videoPlayer.Stop();
        _videoFailed = false;

        if (imageDisplay != null) imageDisplay.gameObject.SetActive(false);
        if (videoDisplay != null) videoDisplay.gameObject.SetActive(true);

        videoPlayer.gameObject.SetActive(true);

        if (!string.IsNullOrWhiteSpace(url))
        {
            videoPlayer.clip = null;
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = url;
        }
        else
        {
            videoPlayer.url = string.Empty;
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip = clip;
        }

        videoPlayer.isLooping = false;
        videoPlayer.playOnAwake = false;
        videoPlayer.loopPointReached += OnVideoFinished;
        videoPlayer.errorReceived += OnVideoError;
        _timerReel?.SetMessage("Loading...", false);
        try { videoPlayer.Prepare(); }
        catch (Exception exception)
        {
            HandleVideoFailure(exception.Message);
            OnCloseClicked();
            return false;
        }

        StartIntroAnimation();
        StartAdRoutine(PrepareAndTrackVideoRoutine);
        return true;
    }

    private void OnRewardedAdClicked()
    {
        if (AdsCallerSalon.Instance != null)
        {
            AdsCallerSalon.Instance.OnRewardedClicked?.Invoke(AdsCallerSalon.Instance.currentRewardedIndex);
        }
    }

    #endregion

    #region Core Ad Logic

    private void GrantReward()
    {
        if (rewardEarned || _videoFailed || !isActiveAndEnabled) return;

        rewardEarned = true;

        // Kill the timer loop so we can reuse the token system for the UI animation
        ResetCancellationToken();

        UnsubscribeVideoEvents();
        _timerReel?.SetMessage("Reward unlocked");

        ShowCloseButton(_showStoreArrowBeforeClose);

        // Fire off the width animation async routine
        StartAdRoutine(AnimateTextBodySizeAsync);
    }

    private void OnVideoFinished(VideoPlayer vp)
    {
        UnsubscribeVideoEvents();
        GrantReward();
    }

    private void OnVideoError(VideoPlayer vp, string message)
    {
        HandleVideoFailure($"Video playback failed: {message}");
    }

    public void OnCloseClicked()
    {
        if (!gameObject.activeSelf) return;
        if (_closeControl.HandleArrowClick(OnRewardedAdClicked)) return;

        ResetCancellationToken();

        Action callback = rewardEarned ? onSuccess : onFail;
        onSuccess = null;
        onFail = null;

        gameObject.SetActive(false);
        callback?.Invoke();
    }

    #endregion

    #region Async Routines

    private void StartAdRoutine(Func<CancellationToken, Awaitable> routine)
    {
        adRoutineCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        _ = routine(adRoutineCts.Token);
    }

    /// <summary>
    /// Animates the rewarded ad entry by sequentially fading the canvas and sliding the active media up.
    /// </summary>
    private void StartIntroAnimation()
    {
        CancelIntroAnimation();
        introCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        _ = PlayIntroAnimationAsync(introCts.Token);
    }

    private void CancelIntroAnimation()
    {
        if (introCts == null) return;
        introCts.Cancel();
        introCts.Dispose();
        introCts = null;
    }

    private async Awaitable PlayIntroAnimationAsync(CancellationToken token)
    {
        try
        {
            _canvasGroup.alpha = 0f;

            // PRE-SETUP: Teleport the active media down immediately before the fade starts
            RectTransform activeRect = imageDisplay != null && imageDisplay.gameObject.activeSelf ? imageDisplay.rectTransform : (videoDisplay != null ? videoDisplay.rectTransform : null);
            Vector2 targetPos = Vector2.zero;
            Vector2 startPos = Vector2.zero;

            if (activeRect != null)
            {
                targetPos = imageDisplay != null && imageDisplay.gameObject.activeSelf ? _originalImagePos : _originalVideoPos;
                startPos = targetPos + new Vector2(0, -2000f);

                // Snap it down instantly while alpha is still 0
                activeRect.anchoredPosition = startPos;
            }

            // Step 1: Fade in the canvas globally
            float fadeDuration = 0.2f;
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Clamp01(elapsed / fadeDuration);
                await Awaitable.NextFrameAsync(token);
            }
            _canvasGroup.alpha = 1f;

            // Step 2: Slide up the active media
            if (activeRect != null)
            {
                float slideDuration = 0.5f;
                elapsed = 0f;

                while (elapsed < slideDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / slideDuration);

                    // Ease Out Quad for a smooth deceleration at the top
                    float easeT = 1f - (1f - t) * (1f - t);

                    activeRect.anchoredPosition = Vector2.Lerp(startPos, targetPos, easeT);
                    await Awaitable.NextFrameAsync(token);
                }

                activeRect.anchoredPosition = targetPos;
            }
        }
        catch (OperationCanceledException)
        {
            Debug.Log("[OutSalonRewardedAd] Intro animation cancelled because the object was destroyed.");
        }
    }

    private async Awaitable ImageTimerRoutine(CancellationToken token)
    {
        currentTimer = SafeDuration(imageRewardDuration, 5f);

        while (currentTimer > 0)
        {
            if (token.IsCancellationRequested) return;

            _timerReel?.SetCountdown(Mathf.CeilToInt(currentTimer), "Reward in", "s");

            try
            {
                await Awaitable.NextFrameAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Using unscaledDeltaTime because timeScale is 0
            currentTimer -= Time.unscaledDeltaTime;
        }

        GrantReward();
    }

    private async Awaitable PrepareAndTrackVideoRoutine(CancellationToken token)
    {
        float elapsed = 0f;

        try
        {
            while (videoPlayer != null && !videoPlayer.isPrepared && !_videoFailed)
            {
                if (elapsed >= Mathf.Max(1f, SafeDuration(videoPreparationTimeout, 15f)))
                {
                    HandleVideoFailure("Video preparation timed out.");
                    return;
                }

                await Awaitable.NextFrameAsync(token);
                elapsed += Time.unscaledDeltaTime;
            }

            if (token.IsCancellationRequested || _videoFailed) return;
            if (videoPlayer == null) { HandleVideoFailure("Video player disappeared."); return; }

            videoPlayer.Play();
            await VideoTimerUpdateRoutine(token);
        }
        catch (OperationCanceledException)
        {
            // The ad was closed, replaced, or destroyed.
        }
        catch (Exception exception) { HandleVideoFailure(exception.Message); }
    }

    private async Awaitable VideoTimerUpdateRoutine(CancellationToken token)
    {
        if (videoPlayer == null) return;

        float videoLength = (float)videoPlayer.length;
        bool hasKnownLength = videoLength > 0f && !float.IsNaN(videoLength) && !float.IsInfinity(videoLength);
        float requiredWatchTime = hasKnownLength ? videoLength : float.PositiveInfinity;

        // If the video is over 10 seconds, reduce the required watch time by 70%
        if (hasKnownLength && videoLength > 10f)
        {
            requiredWatchTime = videoLength * 0.3f;
            Logger.Note($"[OutSalonRewardedAd] Video is {videoLength}s. Cutting timer by 70%. New target: {requiredWatchTime}s.");
        }

        float lastVideoTime = (float)videoPlayer.time;
        float stalledFor = 0f;
        float unknownElapsed = 0f;

        // Track playback progress rather than isPlaying alone. isPlaying may briefly become false
        // while a stream buffers, and previously that left the ad permanently unclosable.
        while (!rewardEarned && videoPlayer != null && !_videoFailed)
        {
            if (token.IsCancellationRequested) return;

            float videoTime = (float)videoPlayer.time;
            float timeRemaining = Mathf.Max(0, requiredWatchTime - videoTime);

            if (hasKnownLength)
                _timerReel?.SetCountdown(Mathf.CeilToInt(timeRemaining), "Reward in", "s");
            else
                _timerReel?.SetMessage("Playing video...", false);

            unknownElapsed += Time.unscaledDeltaTime;
            if (!hasKnownLength && unknownElapsed >= Mathf.Max(1f, SafeDuration(unknownVideoDurationTimeout, 120f)))
            {
                HandleVideoFailure("Video duration/completion was unavailable for too long.");
                return;
            }

            if (hasKnownLength && timeRemaining <= 0f)
            {
                GrantReward();
                return;
            }

            if (videoTime > lastVideoTime + 0.01f)
            {
                lastVideoTime = videoTime;
                stalledFor = 0f;
            }
            else
            {
                stalledFor += Time.unscaledDeltaTime;
                if (stalledFor >= Mathf.Max(1f, SafeDuration(videoStallTimeout, 15f)))
                {
                    HandleVideoFailure("Video playback stalled.");
                    return;
                }
            }

            await Awaitable.NextFrameAsync(token);
        }
        if (!rewardEarned && !_videoFailed && isActiveAndEnabled)
            HandleVideoFailure("Video player disappeared during playback.");
    }

    private async Awaitable AnimateTextBodySizeAsync(CancellationToken token)
    {
        if (textBody == null) return;

        float elapsedTime = 0f;
        Vector2 startSize = textBody.sizeDelta;

        // Target vectors now lock onto both your maximum limits
        Vector2 targetSize = new Vector2(maxWidth, maxHeight);

        while (elapsedTime < SafeDuration(animationDuration, 0.5f))
        {
            if (token.IsCancellationRequested) return;

            elapsedTime += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsedTime / Mathf.Max(0.001f, SafeDuration(animationDuration, 0.5f)));

            textBody.sizeDelta = Vector2.Lerp(startSize, targetSize, t);

            try
            {
                await Awaitable.NextFrameAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        // Snap to the absolute target to prevent floating-point slop
        textBody.sizeDelta = targetSize;
    }

    #endregion

    #region State Management & Cleanup

    private void ResetAdState(Action successCallback, Action failCallback)
    {
        CancelIntroAnimation();
        ResetCancellationToken();

        onSuccess = successCallback;
        onFail = failCallback;
        rewardEarned = false;
        _videoFailed = false;
        _closeControl.Hide();
        _timerReel?.Clear();

        if (closeButton != null)
            closeButton.gameObject.SetActive(false);

        // Reset Text Body width and height to minWidth immediately
        if (textBody != null)
        {
            textBody.sizeDelta = new Vector2(minWidth, minHeight);
        }

        Time.timeScale = 0;
        AudioListener.volume = 0;
    }

    private void ResetCancellationToken()
    {
        if (adRoutineCts != null)
        {
            adRoutineCts.Cancel();
            adRoutineCts.Dispose();
            adRoutineCts = null;
        }
    }

    private void UnsubscribeVideoEvents()
    {
        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= OnVideoFinished;
            videoPlayer.errorReceived -= OnVideoError;
        }
    }

    private void HandleVideoFailure(string message)
    {
        if (_videoFailed || rewardEarned || !isActiveAndEnabled) return;

        _videoFailed = true;
        CancelIntroAnimation();
        Debug.LogError($"[OutSalonRewardedAd] {message}");
        ResetCancellationToken();
        UnsubscribeVideoEvents();

        if (videoPlayer != null) videoPlayer.Stop();
        _canvasGroup.alpha = 1f;
        _timerReel?.SetMessage("Video unavailable", false);
        ShowCloseButton();
    }

    private void ShowCloseButton(bool showArrow = false)
    {
        _closeControl.Show(showArrow);
    }

    private static float SafeDuration(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(0f, value);
    }

    private void RestoreGlobalStates()
    {
        if (!_globalStatesCaptured) return;

        Time.timeScale = _previousTimeScale;
        AudioListener.volume = _previousAudioVolume;
        _globalStatesCaptured = false;
    }

    private void OnDisable()
    {
        CancelIntroAnimation();
        ResetCancellationToken();
        UnsubscribeVideoEvents();
        RestoreGlobalStates();

        if (videoPlayer != null)
        {
            videoPlayer.Stop();
        }

        Action callback = rewardEarned ? onSuccess : onFail;
        onSuccess = null;
        onFail = null;
        callback?.Invoke();
    }

    private void OnDestroy()
    {
        CancelIntroAnimation();
        ResetCancellationToken();
        UnsubscribeVideoEvents();
        RestoreGlobalStates();

        if (mainAdButton != null)
            mainAdButton.onClick.RemoveListener(OnRewardedAdClicked);

        if (closeButton != null)
            closeButton.onClick.RemoveListener(OnCloseClicked);
    }

    #endregion
}
