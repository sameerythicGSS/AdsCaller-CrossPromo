using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Unscaled, presentation-only slot reels. Never owns the ad's clock or completion.</summary>
[DisallowMultipleComponent]
public sealed class SalonAdTimerReel : MonoBehaviour
{
    private sealed class Digit
    {
        public RectTransform window;
        public Text current;
        public Text next;
        public char value = ' ';
        public bool rolling;
    }

    private const float Duration = 0.2f;
    private readonly List<Digit> digits = new List<Digit>();
    private Text source, prefixLabel, suffixLabel, message;
    private RectTransform viewport, countdown, messageRect;
    private string prefix = "", suffix = "";
    private int lastSeconds = -1, places;
    private float elapsed;
    private bool transitioningMessage, showingMessage, initialized;
    private Vector2 lastSize;

    public static SalonAdTimerReel Attach(Text text)
    {
        if (text == null) return null;
        var reel = text.GetComponent<SalonAdTimerReel>();
        if (reel == null) reel = text.gameObject.AddComponent<SalonAdTimerReel>();
        reel.Initialize(text);
        return reel;
    }

    private void Initialize(Text text)
    {
        if (initialized) return;
        initialized = true;
        source = text;
        viewport = Rect("Timer reel", transform);
        viewport.gameObject.AddComponent<RectMask2D>();
        countdown = Rect("Countdown", viewport);
        prefixLabel = Label("Prefix", countdown);
        suffixLabel = Label("Suffix", countdown);
        message = Label("Message", viewport);
        message.resizeTextForBestFit = true;
        messageRect = message.rectTransform;
        source.enabled = false;
        source.raycastTarget = false;
        Clear();
    }

    public void Clear()
    {
        if (!initialized) return;
        lastSeconds = -1;
        places = 0;
        showingMessage = transitioningMessage = false;
        source.text = "";
        countdown.anchoredPosition = Vector2.zero;
        messageRect.anchoredPosition = Vector2.zero;
        countdown.gameObject.SetActive(false);
        message.gameObject.SetActive(false);
        foreach (var digit in digits) digit.window.gameObject.SetActive(false);
    }

    public void SetCountdown(int seconds, string label = "", string units = "")
    {
        seconds = Mathf.Max(0, seconds);
        if (!showingMessage && lastSeconds == seconds && prefix == label && suffix == units) return;
        bool animate = lastSeconds >= 0 && !showingMessage && isActiveAndEnabled;
        FinishMotion();
        prefix = label;
        suffix = units;
        source.text = (label.Length == 0 ? "" : label + " ") + seconds + units;
        string value = seconds.ToString();
        places = Mathf.Max(places, value.Length);
        while (digits.Count < places) AddDigit();
        value = value.PadLeft(places);
        prefixLabel.text = label;
        suffixLabel.text = units;
        for (int i = 0; i < digits.Count; i++)
        {
            var digit = digits[i];
            digit.window.gameObject.SetActive(i < places);
            if (i >= places) continue;
            char nextValue = value[i];
            digit.rolling = animate && digit.value != nextValue;
            digit.next.text = nextValue.ToString();
            if (!digit.rolling) digit.current.text = nextValue.ToString();
            digit.value = nextValue;
            digit.next.gameObject.SetActive(digit.rolling);
        }
        showingMessage = transitioningMessage = false;
        countdown.gameObject.SetActive(true);
        message.gameObject.SetActive(false);
        lastSeconds = seconds;
        elapsed = 0f;
        Layout();
        PositionMotion(0f);
    }

    public void SetMessage(string value, bool animate = true)
    {
        if (showingMessage && message.text == value) return;
        FinishMotion();
        source.text = value;
        message.text = value;
        transitioningMessage = animate && lastSeconds >= 0 && isActiveAndEnabled;
        showingMessage = true;
        message.gameObject.SetActive(true);
        countdown.gameObject.SetActive(transitioningMessage);
        elapsed = 0f;
        Layout();
        PositionMotion(transitioningMessage ? 0f : 1f);
    }

    private void LateUpdate()
    {
        if (!initialized) return;
        if (lastSize != viewport.rect.size) Layout();
        if (elapsed >= Duration) return;
        elapsed = Mathf.Min(Duration, elapsed + Time.unscaledDeltaTime);
        float t = elapsed / Duration;
        float eased = 1f - (1f - t) * (1f - t) * (1f - t);
        PositionMotion(eased);
        if (elapsed >= Duration) FinishMotion();
    }

    private void PositionMotion(float t)
    {
        foreach (var digit in digits)
        {
            if (!digit.rolling) continue;
            float height = digit.window.rect.height;
            digit.current.rectTransform.anchoredPosition = Vector2.up * (height * t);
            digit.next.rectTransform.anchoredPosition = Vector2.down * (height * (1f - t));
        }
        if (transitioningMessage)
        {
            countdown.anchoredPosition = Vector2.up * (viewport.rect.height * t);
            messageRect.anchoredPosition = Vector2.down * (viewport.rect.height * (1f - t));
        }
    }

    private void FinishMotion()
    {
        elapsed = Duration;
        foreach (var digit in digits)
        {
            if (digit.rolling) digit.current.text = digit.value.ToString();
            digit.rolling = false;
            digit.current.rectTransform.anchoredPosition = Vector2.zero;
            digit.next.gameObject.SetActive(false);
        }
        countdown.anchoredPosition = messageRect.anchoredPosition = Vector2.zero;
        if (showingMessage) countdown.gameObject.SetActive(false);
        transitioningMessage = false;
    }

    private void OnDisable() { if (initialized) FinishMotion(); }

    private void AddDigit()
    {
        var window = Rect("Digit " + digits.Count, countdown);
        window.gameObject.AddComponent<RectMask2D>();
        digits.Add(new Digit { window = window, current = Label("Current", window), next = Label("Next", window) });
    }

    private void Layout()
    {
        lastSize = viewport.rect.size;
        float width = Mathf.Max(1f, lastSize.x);
        float height = Mathf.Max(1f, lastSize.y);
        float numberHeight = prefix.Length == 0 ? height : height * 0.6f;
        int maxFont = source.resizeTextForBestFit ? source.resizeTextMaxSize : source.fontSize;
        int numberFont = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(maxFont, numberHeight * 0.7f, width / (Mathf.Max(1, places) + suffix.Length) * 1.25f)));
        float cellWidth = numberFont * 0.8f;
        float totalWidth = (places + suffix.Length) * cellWidth;
        for (int i = 0; i < digits.Count; i++)
        {
            var digit = digits[i];
            digit.window.anchorMin = digit.window.anchorMax = new Vector2(0.5f, 0f);
            digit.window.pivot = new Vector2(0.5f, 0f);
            digit.window.sizeDelta = new Vector2(cellWidth, numberHeight);
            digit.window.anchoredPosition = new Vector2(-totalWidth * 0.5f + cellWidth * (i + 0.5f), 0f);
            digit.current.fontSize = digit.next.fontSize = numberFont;
        }
        prefixLabel.gameObject.SetActive(prefix.Length > 0);
        prefixLabel.rectTransform.anchorMin = new Vector2(0f, 0.6f);
        prefixLabel.rectTransform.anchorMax = Vector2.one;
        prefixLabel.fontSize = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(maxFont, height * 0.26f, width / Mathf.Max(1, prefix.Length) * 1.55f)));
        suffixLabel.rectTransform.anchorMin = suffixLabel.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        suffixLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
        suffixLabel.rectTransform.sizeDelta = new Vector2(cellWidth * suffix.Length, numberHeight);
        suffixLabel.rectTransform.anchoredPosition = new Vector2(totalWidth * 0.5f - cellWidth * suffix.Length * 0.5f, 0f);
        suffixLabel.fontSize = numberFont;
    }

    private Text Label(string name, Transform parent)
    {
        var rect = Rect(name, parent);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = source.font;
        text.fontSize = source.fontSize;
        text.fontStyle = source.fontStyle;
        text.color = source.color;
        text.material = source.material;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextMinSize = source.resizeTextMinSize;
        text.resizeTextMaxSize = source.resizeTextForBestFit ? source.resizeTextMaxSize : source.fontSize;
        text.supportRichText = false;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.gameObject.layer = parent.gameObject.layer;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
}
