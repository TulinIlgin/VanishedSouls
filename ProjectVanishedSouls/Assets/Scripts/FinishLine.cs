using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Bitiş Çizgisi (Finish Line) scripti.
// Aria bitiş çizgisine ulaştığında ekranı yumuşak bir şekilde karartır (Fade to Black)
// ve varsa 2. Level sahnesine geçiş yapar.
public class FinishLine : MonoBehaviour
{
    [Header("Bitiş Çizgisi Algılama Alanı")]
    [Tooltip("Bitiş çizgisi genişliği (metre)")]
    public float lineWidth = 4.2f;
    [Tooltip("Bitiş çizgisi derinliği (metre)")]
    public float lineDepth = 1.2f;

    [Header("Ekran Kararma ve 2. Level Geçiş Ayarları")]
    [Tooltip("Bitiş çizgisine ulaşınca ekranın kararma süresi (saniye)")]
    public float fadeDuration = 1.5f;
    [Tooltip("Ekran karardıktan sonra yüklenecek 2. Level sahnesinin adı (boş bırakılırsa ekran kararır ve bölüm biter)")]
    public string nextLevelSceneName = "";

    [Header("Durum ve Eventler")]
    public bool isCompleted = false;
    [Tooltip("Bitiş çizgisine ulaşıldığında tetiklenecek ekstra UI/Oyun eventleri")]
    public UnityEvent onFinishReached;

    AriaController aria;

    public static FinishLine EnsureOnFinalPoint(Transform finalPoint)
    {
        if (finalPoint == null) return null;
        var oldVis = finalPoint.Find("FinishLineVisual");
        if (oldVis != null)
        {
            if (Application.isPlaying) Destroy(oldVis.gameObject);
            else DestroyImmediate(oldVis.gameObject);
        }

        var fl = finalPoint.GetComponent<FinishLine>();
        if (fl == null) fl = finalPoint.gameObject.AddComponent<FinishLine>();
        return fl;
    }

    void Awake()
    {
        isCompleted = false;
    }

    void Start()
    {
        aria = FindAnyObjectByType<AriaController>();
    }

    void Update()
    {
        if (isCompleted || GameMenuManager.IsMenuOpen) return;

        if (aria == null)
            aria = FindAnyObjectByType<AriaController>();
        if (aria == null) return;

        var pc = aria.GetComponent<PlayerCharacter>();
        if (pc != null && pc.IsDead) return;

        // Konum bazlı olarak Aria'nın bitiş çizgisinden geçtiğini algılar
        Vector3 localPos = transform.InverseTransformPoint(aria.transform.position);
        float halfW = lineWidth * 0.5f;
        float halfD = lineDepth * 0.5f;

        if (Mathf.Abs(localPos.x) <= halfW && Mathf.Abs(localPos.z) <= halfD)
        {
            CompleteLevel();
        }
    }

    // Objeye BoxCollider (IsTrigger = true) eklenirse fiziksel Trigger ile de çalışır
    void OnTriggerEnter(Collider other)
    {
        if (isCompleted) return;
        if (other.GetComponentInParent<AriaController>() != null)
        {
            CompleteLevel();
        }
    }

    public void CompleteLevel()
    {
        if (isCompleted) return;
        isCompleted = true;

        if (aria != null) aria.Stop();

        Debug.Log("Bitiş Çizgisine ulaşıldı! Ekran kararıyor (2. Levele geçiş)...");
        StartCoroutine(FadeToBlackAndTransitionRoutine());
    }

    IEnumerator FadeToBlackAndTransitionRoutine()
    {
        // Tam ekran siyah kararma (Fade) katmanı oluştur
        var canvasObj = new GameObject("LevelTransitionFadeCanvas");
        canvasObj.layer = 2; // Ignore Raycast
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90; // Oyun ekranının üstünde, varsa Victory UI panelinin altında/üstünde

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var fadeObj = new GameObject("BlackFadeImage");
        fadeObj.transform.SetParent(canvasObj.transform, false);
        var rect = fadeObj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var fadeImg = fadeObj.AddComponent<Image>();
        fadeImg.color = new Color(0f, 0f, 0f, 0f);
        fadeImg.raycastTarget = true;

        // Ekranı yumuşakça karart (0 -> 1 Alpha)
        float duration = Mathf.Max(0.2f, fadeDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float alpha = Mathf.Clamp01(elapsed / duration);
            fadeImg.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }
        fadeImg.color = new Color(0f, 0f, 0f, 1f);

        onFinishReached?.Invoke();
        GameMenuManager.ShowVictory("Bitiş çizgisine ulaştın! 2. Levele geçiliyor...");

        // Eğer Inspector'dan 2. Level sahne adı girildiyse kararma tamamlanınca o sahneye geç
        if (!string.IsNullOrEmpty(nextLevelSceneName))
        {
            yield return new WaitForSecondsRealtime(0.4f);
            Time.timeScale = 1f;
            SceneManager.LoadScene(nextLevelSceneName);
        }
    }

    // Sadece Scene (Editör) ekranında yerini görebilmeniz için yeşil tel çerçeve çizer
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.85f);
        Matrix4x4 oldMat = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(new Vector3(0f, 0.25f, 0f), new Vector3(lineWidth, 0.5f, lineDepth));
        Gizmos.matrix = oldMat;
    }
}
