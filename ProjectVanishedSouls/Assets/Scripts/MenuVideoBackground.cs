using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Başlangıç menüsünde (Start butonunun arkasında) tam ekran döngüsel (loop) video oynatır.
/// Start butonunu otomatik bulup bağlar ve diğer görsellerin butona tıklamayı engellemesini önler.
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class MenuVideoBackground : MonoBehaviour
{
    [Header("Arka Plan Videosu")]
    [Tooltip("Butonların arkasında döngü (loop) olarak oynatılacak video dosyası (.mp4, .webm vb.)")]
    public VideoClip backgroundVideo;

    [Header("Video Ses Ayarları")]
    [Tooltip("Videonun kendi sesi kapatılsın mı?")]
    public bool muteVideoAudio = false;
    [Range(0f, 1f)] public float videoVolume = 0.6f;

    [Header("Menü Paneli (Opsiyonel)")]
    [Tooltip("Boş bırakılırsa GameMenuManager'daki Main Menu Panel veya bulunduğu UI paneli kullanılır")]
    public GameObject menuPanelToClose;

    VideoPlayer videoPlayer;
    RawImage targetRawImage;
    RenderTexture renderTexture;
    AudioSource videoAudioSource;
    Transform uiRoot;
    Button startButton;
    bool menuStartedClosed;

    void Awake()
    {
        var menuMgr = GetComponent<GameMenuManager>() ?? FindAnyObjectByType<GameMenuManager>();
        if (menuMgr != null)
        {
            menuMgr.startInMainMenu = true;
            if (menuMgr.mainMenuPanel != null)
                menuMgr.mainMenuPanel.SetActive(true);
        }

        SetupVideoDisplay(menuMgr);
        SetupVideoPlayer();
        AutoBindStartButton();
    }

    void OnEnable()
    {
        if (videoPlayer != null && backgroundVideo != null && !menuStartedClosed)
        {
            videoPlayer.Play();
        }
    }

    void SetupVideoDisplay(GameMenuManager menuMgr)
    {
        uiRoot = transform;
        if (menuMgr != null && menuMgr.mainMenuPanel != null)
        {
            uiRoot = menuMgr.mainMenuPanel.transform;
            if (menuPanelToClose == null) menuPanelToClose = menuMgr.mainMenuPanel;
        }
        else if (menuPanelToClose != null)
        {
            uiRoot = menuPanelToClose.transform;
        }
        else if (GetComponent<RectTransform>() == null)
        {
            var canvas = FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas != null)
            {
                canvas.gameObject.SetActive(true);
                uiRoot = canvas.transform;
                if (menuPanelToClose == null) menuPanelToClose = canvas.gameObject;
            }
        }

        // Buton olmayan tüm Image'ların (Panel ve Oyun Adı Logosu gibi) fare tıklamasını bloklamasını engelle!
        foreach (var img in uiRoot.GetComponentsInChildren<Image>(true))
        {
            if (img == null) continue;
            if (img.GetComponent<Button>() == null)
            {
                img.raycastTarget = false; // Logo veya Panel, Start butonunun üstünü kapatsa bile tıklamayı engellemez!
                if (img.gameObject.name == "Panel")
                {
                    img.color = new Color(1f, 1f, 1f, 0f);
                }
            }
        }

        targetRawImage = uiRoot.GetComponent<RawImage>();

        if (targetRawImage == null)
        {
            var bgObj = new GameObject("VideoBackgroundLayer");
            bgObj.transform.SetParent(uiRoot, false);
            bgObj.transform.SetAsFirstSibling(); // En arkaya koy

            var rect = bgObj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            targetRawImage = bgObj.AddComponent<RawImage>();
            targetRawImage.raycastTarget = false;
        }

        renderTexture = new RenderTexture(1920, 1080, 0);
        renderTexture.Create();
        targetRawImage.texture = renderTexture;
        targetRawImage.color = Color.white;
    }

    void AutoBindStartButton()
    {
        if (uiRoot == null) return;

        foreach (var btn in uiRoot.GetComponentsInChildren<Button>(true))
        {
            if (btn == null) continue;
            startButton = btn;
            btn.interactable = true;
            if (btn.targetGraphic != null)
                btn.targetGraphic.raycastTarget = true;

            btn.onClick.RemoveListener(OnStartButtonClicked);
            btn.onClick.AddListener(OnStartButtonClicked);
            break;
        }
    }

    void SetupVideoPlayer()
    {
        videoPlayer = GetComponent<VideoPlayer>();
        videoPlayer.playOnAwake = true;
        videoPlayer.isLooping = true;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = renderTexture;
        videoPlayer.aspectRatio = VideoAspectRatio.FitOutside;
        videoPlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;

        if (backgroundVideo != null)
        {
            videoPlayer.clip = backgroundVideo;
        }

        videoAudioSource = GetComponent<AudioSource>();
        if (videoAudioSource == null)
            videoAudioSource = gameObject.AddComponent<AudioSource>();

        videoAudioSource.playOnAwake = false;
        videoAudioSource.spatialBlend = 0f;
        videoAudioSource.mute = muteVideoAudio;
        videoAudioSource.volume = videoVolume;

        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        videoPlayer.EnableAudioTrack(0, true);
        videoPlayer.SetTargetAudioSource(0, videoAudioSource);
    }

    void Start()
    {
        if (backgroundVideo != null && !menuStartedClosed)
        {
            videoPlayer.clip = backgroundVideo;
            videoPlayer.Play();
        }
    }

    void Update()
    {
        if (videoAudioSource != null)
        {
            videoAudioSource.mute = muteVideoAudio;
            videoAudioSource.volume = videoVolume;
        }

        if (menuStartedClosed) return;

        // Ekstra güvenlik: EventSystem veya başka bir katman engellese bile Start butonunun alanına sol tıklanırsa oyunu başlat!
        if (startButton != null && startButton.gameObject.activeInHierarchy && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            RectTransform btnRect = startButton.GetComponent<RectTransform>();
            Vector2 mousePos = Mouse.current.position.ReadValue();
            if (btnRect != null && RectTransformUtility.RectangleContainsScreenPoint(btnRect, mousePos, null))
            {
                OnStartButtonClicked();
            }
        }
    }

    /// <summary>
    /// Start butonuna basıldığında videoyu ve menüyü kapatıp oyunu başlatır.
    /// </summary>
    public void OnStartButtonClicked()
    {
        if (menuStartedClosed) return;
        menuStartedClosed = true;

        if (videoPlayer != null)
            videoPlayer.Stop();

        if (videoAudioSource != null)
            videoAudioSource.Stop();

        if (targetRawImage != null)
            targetRawImage.gameObject.SetActive(false);

        Time.timeScale = 1f;

        if (GameMenuManager.Instance != null)
        {
            GameMenuManager.Instance.StartGame();
        }

        if (menuPanelToClose != null)
        {
            menuPanelToClose.SetActive(false);
        }

        if (uiRoot != null)
        {
            uiRoot.gameObject.SetActive(false);
        }
    }

    void OnDestroy()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }
}
