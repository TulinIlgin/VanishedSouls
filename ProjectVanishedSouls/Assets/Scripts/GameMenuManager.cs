using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Arkadaşının yaptığı UI tasarımına (Butonların OnClick eventlerine ve Panellere) bağlanacak Menü Yöneticisi kodu.
// Kendi başına ekrana görsel çizmez; Inspector'dan atanan panelleri ve sahne geçişlerini yönetir.
public class GameMenuManager : MonoBehaviour
{
    public static GameMenuManager Instance { get; private set; }

    [Header("Sahne İsimleri (Sahne Geçişi İçin)")]
    [Tooltip("Menüden 'Oyuna Başla' denildiğinde yüklenecek oyun sahnesinin adı")]
    public string gameSceneName = "Vanished";
    [Tooltip("'Ana Menüye Dön' denildiğinde yüklenecek menü sahnesinin adı (boşsa mevcut sahnedeki mainMenuPanel açılır)")]
    public string mainMenuSceneName = "";

    [Header("UI Panelleri (Arkadaşın Tasarladığı Panelleri Buraya Sürükleyebilir)")]
    public GameObject mainMenuPanel;
    public GameObject pauseMenuPanel;
    public GameObject victoryPanel;
    public GameObject gameOverPanel;

    [Header("Başlangıç Ayarı")]
    [Tooltip("mainMenuPanel atanmışsa oyun başlarken otomatik olarak Ana Menü açık başlar")]
    public bool startInMainMenu = true;

    // "Yeniden Başlat / Tekrar Dene" denildiğinde sahne yüklenince menüyü tekrar açmadan doğrudan oyuna girmesi için
    static bool skipMainMenuOnReload = false;

    [Header("UI Eventleri (İsteğe Bağlı Inspector Bağlantıları)")]
    public UnityEvent onGameStarted;
    public UnityEvent onGamePaused;
    public UnityEvent onGameResumed;
    public UnityEvent onVictory;
    public UnityEvent onGameOver;

    public bool IsPaused { get; private set; }
    public bool IsGameEnded { get; private set; }

    // Herhangi bir menü paneli açıkken oyun içi tıklamaları durdurmak için kullanılır
    public static bool IsMenuOpen
    {
        get
        {
            if (Instance == null) return false;
            bool mainOpen = Instance.mainMenuPanel != null && Instance.mainMenuPanel.activeInHierarchy;
            return mainOpen || Instance.IsPaused || Instance.IsGameEnded;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        skipMainMenuOnReload = false;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        IsPaused = false;
        IsGameEnded = false;

        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (victoryPanel != null) victoryPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);

        if (startInMainMenu && mainMenuPanel != null && !skipMainMenuOnReload)
        {
            mainMenuPanel.SetActive(true);
            Time.timeScale = 0f;
        }
        else
        {
            skipMainMenuOnReload = false;
            if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
            Time.timeScale = 1f;
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Time.timeScale = 1f;
            Instance = null;
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame && !IsGameEnded)
        {
            if (IsPaused)
                ResumeGame();
            else if (pauseMenuPanel != null)
                PauseGame();
        }
    }

    // ---------- BUTON OnClick() FONKSİYONLARI (UI TASARIMINDA BUTONLARA BAĞLANACAK) ----------

    // "Oyuna Başla" / "Play" butonu için
    public void StartGame()
    {
        Time.timeScale = 1f;
        IsPaused = false;
        IsGameEnded = false;

        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(false);

        // Eğer ayrı bir Menü sahnesindeysek oyun sahnesine geçiş yap
        if (!string.IsNullOrEmpty(gameSceneName) && SceneManager.GetActiveScene().name != gameSceneName)
        {
            SceneManager.LoadScene(gameSceneName);
            return;
        }

        onGameStarted?.Invoke();
    }

    // "Oyuna Başla" için alternatif isim (UI butonunda kolay bulunsun diye)
    public void PlayGame() => StartGame();

    // "Duraklat" / "Pause" butonu veya ESC tuşu için
    public void PauseGame()
    {
        if (IsGameEnded) return;
        IsPaused = true;
        Time.timeScale = 0f;
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(true);
        onGamePaused?.Invoke();
    }

    // "Devam Et" / "Resume" butonu için
    public void ResumeGame()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        onGameResumed?.Invoke();
    }

    // "Tekrar Dene" / "Yeniden Başlat" / "Restart" butonu için
    public void RestartGame()
    {
        Time.timeScale = 1f;
        IsPaused = false;
        IsGameEnded = false;
        skipMainMenuOnReload = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // "Ana Menüye Dön" / "Main Menu" butonu için
    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        IsPaused = false;
        IsGameEnded = false;
        skipMainMenuOnReload = false;

        if (!string.IsNullOrEmpty(mainMenuSceneName) && SceneManager.GetActiveScene().name != mainMenuSceneName)
        {
            SceneManager.LoadScene(mainMenuSceneName);
            return;
        }

        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (victoryPanel != null) victoryPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);

        if (mainMenuPanel != null)
        {
            mainMenuPanel.SetActive(true);
            Time.timeScale = 0f;
        }
        else
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    // "Çıkış" / "Quit" butonu için
    public void QuitGame()
    {
        Time.timeScale = 1f;
        Debug.Log("Oyundan çıkılıyor...");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- OYUN İÇİ TETİKLEYİCİLER (BİTİŞ ÇİZGİSİ VE YAKALANMA) ----------

    public static void ShowVictory(string message = "Bitiş çizgisine ulaştın!")
    {
        Debug.Log($"[BÖLÜM TAMAMLANDI] {message}");
        if (Instance == null) return;
        if (Instance.IsGameEnded) return;

        Instance.IsGameEnded = true;
        if (Instance.victoryPanel != null)
        {
            Instance.victoryPanel.SetActive(true);
            Time.timeScale = 0f;
        }
        Instance.onVictory?.Invoke();
    }

    public static void ShowGameOver(string reason = "Düşman seni yakaladı!")
    {
        Debug.Log($"[OYUN BİTTİ] {reason} -> Sahne yeniden başlatılıyor...");
        Time.timeScale = 1f;
        skipMainMenuOnReload = true;
        SceneManager.LoadScene("Vanished");
    }
}
