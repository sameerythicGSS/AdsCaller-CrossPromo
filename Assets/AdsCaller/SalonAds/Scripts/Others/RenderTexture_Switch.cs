using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class RenderTexture_Switch : MonoBehaviour
{
    [Tooltip("The RenderTexture template used by this video display.")]
    public RenderTexture targetRenderTexture;

    [Tooltip("The VideoPlayer feeding the texture.")]
    public VideoPlayer targetVideoPlayer;

    [Tooltip("If true, the render texture automatically resizes to match the screen resolution.")]
    public bool changeResolutionOnRuntime = true;

    private RenderTexture _runtimeRenderTexture;
    private RawImage _targetDisplay;
    private int _lastScreenWidth;
    private int _lastScreenHeight;

    private void Awake()
    {
        if (targetVideoPlayer == null) targetVideoPlayer = GetComponent<VideoPlayer>();
        _targetDisplay = GetComponent<RawImage>();

        if (targetRenderTexture == null)
        {
            Debug.LogWarning("[RenderTexture_Switch] A RenderTexture template has not been assigned.");
            return;
        }

        // Prefabs share the serialized RenderTexture asset. Mutating that asset at runtime lets one
        // ad release or resize another ad's output, so every display gets its own runtime instance.
        _runtimeRenderTexture = new RenderTexture(targetRenderTexture)
        {
            name = targetRenderTexture.name + " (Runtime)"
        };

        targetRenderTexture = _runtimeRenderTexture;
        if (_targetDisplay != null) _targetDisplay.texture = _runtimeRenderTexture;
        AssignToVideoPlayer();
    }

    private void OnEnable()
    {
        if (_runtimeRenderTexture == null) return;

        if (changeResolutionOnRuntime)
            UpdateRenderTextureResolution();
        else if (!_runtimeRenderTexture.IsCreated())
            _runtimeRenderTexture.Create();
    }

    private void Update()
    {
        if (changeResolutionOnRuntime && _runtimeRenderTexture != null &&
            (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight))
        {
            UpdateRenderTextureResolution();
        }
    }

    private void UpdateRenderTextureResolution()
    {
        _lastScreenWidth = Mathf.Max(1, Screen.width);
        _lastScreenHeight = Mathf.Max(1, Screen.height);

        if (_runtimeRenderTexture.width == _lastScreenWidth &&
            _runtimeRenderTexture.height == _lastScreenHeight &&
            _runtimeRenderTexture.IsCreated())
        {
            return;
        }

        _runtimeRenderTexture.Release();
        _runtimeRenderTexture.width = _lastScreenWidth;
        _runtimeRenderTexture.height = _lastScreenHeight;
        _runtimeRenderTexture.Create();
        AssignToVideoPlayer();
    }

    private void AssignToVideoPlayer()
    {
        if (targetVideoPlayer == null || _runtimeRenderTexture == null) return;

        targetVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
        targetVideoPlayer.targetTexture = _runtimeRenderTexture;
    }

    private void OnDestroy()
    {
        if (targetVideoPlayer != null && targetVideoPlayer.targetTexture == _runtimeRenderTexture)
            targetVideoPlayer.targetTexture = null;

        if (_runtimeRenderTexture == null) return;

        _runtimeRenderTexture.Release();
        Destroy(_runtimeRenderTexture);
        _runtimeRenderTexture = null;
    }
}
