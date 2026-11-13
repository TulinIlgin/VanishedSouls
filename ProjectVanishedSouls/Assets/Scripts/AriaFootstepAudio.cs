using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Aria'nın normal zeminde ve çalı içinde yürürken çıkaracağı ayak seslerini yönetir.
/// Mevcut kodlara dokunmadan doğrudan Aria objesine eklenerek çalışır.
/// </summary>
public class AriaFootstepAudio : MonoBehaviour
{
    [Header("Yürüme Sesleri")]
    [Tooltip("Normal zeminde yürürken çalacak ses (Örn: adimtoprakcakil)")]
    public AudioClip normalWalkSound;

    [Tooltip("Çalıda gizlenerek yürürken çalacak ses (Örn: hareketothisirtisi veya adimgizliceegilme)")]
    public AudioClip bushWalkSound;

    [Header("Ses Ayarları")]
    [Range(0f, 1f)] public float volume = 0.7f;

    AudioSource walkAudioSource;
    NavMeshAgent agent;
    PlayerStealth stealth;
    AriaController aria;

    void Awake()
    {
        // Diğer ses kodlarıyla (bıçak/taş sesleri) çakışmaması için kendine özel AudioSource oluşturur
        walkAudioSource = gameObject.AddComponent<AudioSource>();
        walkAudioSource.playOnAwake = false;
        walkAudioSource.loop = true;
        walkAudioSource.spatialBlend = 0f;

        agent = GetComponent<NavMeshAgent>();
        stealth = GetComponent<PlayerStealth>();
        aria = GetComponent<AriaController>();
    }

    void Update()
    {
        bool isWalking = !GameMenuManager.IsMenuOpen &&
                         (aria == null || !aria.Busy) &&
                         agent != null && agent.enabled &&
                         agent.hasPath && agent.velocity.magnitude > 0.15f;

        if (!isWalking)
        {
            if (walkAudioSource.isPlaying) walkAudioSource.Stop();
            return;
        }

        bool inBush = (stealth != null && stealth.isHidden) || Bush.IsPointInAnyBush(transform.position);
        AudioClip targetClip = inBush ? bushWalkSound : normalWalkSound;

        if (targetClip == null) return;

        walkAudioSource.volume = volume;

        if (walkAudioSource.clip != targetClip || !walkAudioSource.isPlaying)
        {
            walkAudioSource.clip = targetClip;
            walkAudioSource.Play();
        }
    }
}
