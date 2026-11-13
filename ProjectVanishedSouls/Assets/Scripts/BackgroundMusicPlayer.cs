using UnityEngine;

/// <summary>
/// Oyun başladığı anda arka planda kesintisiz (döngü halinde) çalacak müzik ve ortam sesini yönetir.
/// Mevcut kodlara dokunmadan Main Camera veya sahnedeki herhangi bir objeye eklenerek çalışır.
/// </summary>
public class BackgroundMusicPlayer : MonoBehaviour
{
    [Header("Arka Plan Sesi")]
    [Tooltip("Oyun başlar başlamaz arkada sürekli çalacak ana müzik veya ortam sesi")]
    public AudioClip backgroundSound;

    [Header("Ses Ayarları")]
    [Tooltip("Arka plan sesinin yüksekliği (Yürüme ve aksiyon seslerini bastırmaması için 0.35 - 0.5 idealdir)")]
    [Range(0f, 1f)] public float volume = 0.4f;

    AudioSource musicSource;

    void Awake()
    {
        // Diğer seslerle çakışmaması için kendine özel bağımsız bir AudioSource oluşturur
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;        // Oyun boyunca kesintisiz tekrar eder
        musicSource.spatialBlend = 0f;  // 2D ses (Haritanın her yerinde eşit ve net duyulur)
        musicSource.volume = volume;
    }

    void Start()
    {
        if (backgroundSound != null)
        {
            musicSource.clip = backgroundSound;
            musicSource.volume = volume;
            musicSource.Play();
        }
    }

    void Update()
    {
        // Inspector'dan ses seviyesi değiştirilirse anlık olarak uygula
        if (musicSource != null)
        {
            musicSource.volume = volume;
        }
    }
}
