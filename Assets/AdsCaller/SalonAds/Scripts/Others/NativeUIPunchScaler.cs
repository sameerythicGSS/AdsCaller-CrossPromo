using UnityEngine;

public class NativeUIPunchScaler : MonoBehaviour
{
    [Header("Target & Settings")]
    [SerializeField] private RectTransform targetRect;
    [SerializeField] private float delayBetweenPunches = 3f;

    [Header("Punch Configuration")]
    [Tooltip("Maximum scale increase (not more than 0.2)")]
    [SerializeField] private float maxScaleOffset = 0.2f;
    [Tooltip("How fast a single punch (in and out) takes")]
    [SerializeField] private float singlePunchDuration = 0.3f;

    private Vector3 originalScale;
    private float cycleTime;
    private bool initialized;
    private bool animationRequested = true;

    public bool IsAnimating => animationRequested && isActiveAndEnabled && targetRect != null;

    private void Awake()
    {
        InitializeTarget();
    }

    private bool InitializeTarget()
    {
        if (initialized) return targetRect != null;
        if (targetRect == null) targetRect = GetComponent<RectTransform>();
        if (targetRect == null) return false;
        originalScale = targetRect.localScale;
        initialized = true;
        return true;
    }

    private void OnEnable()
    {
        if (InitializeTarget()) ResetPose();
    }

    private void OnDisable()
    {
        if (initialized) ResetPose();
    }

    /// <summary>Start/resume from scripts or UnityEvents, including while inactive.</summary>
    public void StartAnimation()
    {
        if (animationRequested) return;
        animationRequested = true;
        if (InitializeTarget()) ResetPose();
    }

    /// <summary>Stop until explicitly started again; callable from scripts or UnityEvents.</summary>
    public void StopAnimation()
    {
        animationRequested = false;
        if (InitializeTarget()) ResetPose();
    }

    private void ResetPose()
    {
        cycleTime = 0f;
        if (targetRect != null) targetRect.localScale = originalScale;
    }

    private void Update()
    {
        if (!animationRequested || targetRect == null) return;

        // Presentation owns its clock: ad loading/cycling, coroutine cancellation and
        // gameplay pause cannot terminate the animation. Disable only suspends it.
        float delay = Mathf.Max(0f, delayBetweenPunches);
        float duration = Mathf.Max(0.001f, singlePunchDuration);
        float cycleDuration = delay + duration * 2f;
        cycleTime = (cycleTime + Time.unscaledDeltaTime) % cycleDuration;
        float punchTime = cycleTime - delay;
        float amount = 0f;
        if (punchTime >= 0f)
        {
            float phase = (punchTime % duration) / duration;
            amount = 1f - Mathf.Abs(phase * 2f - 1f);
        }
        targetRect.localScale = originalScale + Vector3.one * (maxScaleOffset * amount);
    }
}
