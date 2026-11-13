using System.Reflection;
using UnityEngine;

/// <summary>
/// Aria'nın aksiyon seslerini (Bıçak Çekme, Bıçak Saplama, Taş Alma, Taş Fırlatma) yönetir.
/// Ses dosyalarının başındaki ölü sessizlik boşluklarını otomatik kırparak tam aksiyon anında (0 gecikmeyle) çalar.
/// </summary>
public class AriaActionAudio : MonoBehaviour
{
    [Header("Bıçak Sesleri")]
    [Tooltip("F tuşuyla bıçağı elimize aldığımızda çalacak ses")]
    public AudioClip knifeDrawSound;

    [Tooltip("Bıçağı düşmana sapladığımız anda çalacak ses")]
    public AudioClip knifeStabSound;

    [Header("Taş Sesleri")]
    [Tooltip("E tuşuyla yerden taşı elimize aldığımızda çalacak ses")]
    public AudioClip stonePickupSound;

    [Tooltip("Sağ tıkla taşı elimizden fırlattığımız anda çalacak ses")]
    public AudioClip stoneThrowSound;

    [Header("Ses Ayarları")]
    [Range(0f, 1f)] public float volume = 0.9f;

    AudioSource actionAudioSource;
    AriaController aria;
    FieldInfo pendingKnifeTargetField;

    AudioClip trimmedKnifeDraw;
    AudioClip trimmedKnifeStab;
    AudioClip trimmedStonePickup;
    AudioClip trimmedStoneThrow;

    bool wasKnifeMode;
    bool hadStone;
    bool hadPendingKnifeTarget;

    void Awake()
    {
        actionAudioSource = gameObject.AddComponent<AudioSource>();
        actionAudioSource.playOnAwake = false;
        actionAudioSource.loop = false;
        actionAudioSource.spatialBlend = 0f;

        aria = GetComponent<AriaController>();
        pendingKnifeTargetField = typeof(AriaController).GetField("pendingKnifeTarget", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    void Start()
    {
        // MP3 dosyalarının başındaki sessiz boşlukları otomatik kırp (Gecikmeyi sıfırlar)
        trimmedKnifeDraw = TrimLeadingSilence(knifeDrawSound);
        trimmedKnifeStab = TrimLeadingSilence(knifeStabSound);
        trimmedStonePickup = TrimLeadingSilence(stonePickupSound);
        trimmedStoneThrow = TrimLeadingSilence(stoneThrowSound);

        if (aria != null)
        {
            wasKnifeMode = aria.KnifeMode;
            hadStone = aria.HasStone;
            hadPendingKnifeTarget = HasKnifeTarget();
        }
    }

    void LateUpdate()
    {
        if (aria == null) return;

        bool isKnifeMode = aria.KnifeMode;
        bool hasStone = aria.HasStone;
        bool hasKnifeTarget = HasKnifeTarget();

        // 1. Bıçağı tam çektiğimiz an (F tuşuna basıldığı kare)
        if (isKnifeMode && !wasKnifeMode)
        {
            PlayInstant(trimmedKnifeDraw ?? knifeDrawSound);
        }

        // 2. Bıçağı düşmana tam sapladığımız an (OnKnifeHit vuruş karesi)
        if (!hasKnifeTarget && hadPendingKnifeTarget)
        {
            PlayInstant(trimmedKnifeStab ?? knifeStabSound);
        }

        // 3. Yerden taşı tam elimize aldığımız an (Eğilip elin taşa değdiği kare)
        if (hasStone && !hadStone)
        {
            PlayInstant(trimmedStonePickup ?? stonePickupSound);
        }

        // 4. Taşı elimizden tam fırlattığımız an (OnThrowRelease fırlatma karesi)
        if (!hasStone && hadStone)
        {
            PlayInstant(trimmedStoneThrow ?? stoneThrowSound);
        }

        wasKnifeMode = isKnifeMode;
        hadStone = hasStone;
        hadPendingKnifeTarget = hasKnifeTarget;
    }

    bool HasKnifeTarget()
    {
        if (aria == null || pendingKnifeTargetField == null) return false;
        var val = pendingKnifeTargetField.GetValue(aria) as Transform;
        return val != null;
    }

    void PlayInstant(AudioClip clip)
    {
        if (clip != null && actionAudioSource != null)
        {
            actionAudioSource.PlayOneShot(clip, volume);
        }
    }

    /// <summary>
    /// Ses dosyasının başındaki sessiz boşluğu tespit edip keser; sesin 0.00 saniyede anında başlamasını sağlar.
    /// </summary>
    static AudioClip TrimLeadingSilence(AudioClip clip)
    {
        if (clip == null || clip.samples <= 0) return clip;

        try
        {
            int channels = clip.channels;
            int totalSamples = clip.samples;
            float[] data = new float[totalSamples * channels];

            if (!clip.GetData(data, 0))
                return clip;

            // Sesin tepe yüksekliğini bul
            float maxAmp = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float a = Mathf.Abs(data[i]);
                if (a > maxAmp) maxAmp = a;
            }
            if (maxAmp < 0.001f) return clip;

            // Ses dalgasının gerçekten başladığı ilk kareyi bul (%8 eşik)
            float threshold = Mathf.Max(0.015f, maxAmp * 0.08f);
            int firstActiveFrame = 0;
            for (int frame = 0; frame < totalSamples; frame++)
            {
                bool active = false;
                int baseIdx = frame * channels;
                for (int c = 0; c < channels; c++)
                {
                    if (Mathf.Abs(data[baseIdx + c]) >= threshold)
                    {
                        active = true;
                        break;
                    }
                }
                if (active)
                {
                    firstActiveFrame = frame;
                    break;
                }
            }

            // Başta 15ms'den az boşluk varsa kırpmaya gerek yok
            int marginFrames = Mathf.RoundToInt(clip.frequency * 0.008f);
            int startFrame = Mathf.Max(0, firstActiveFrame - marginFrames);
            if (startFrame <= marginFrames) return clip;

            int newSampleCount = totalSamples - startFrame;
            float[] trimmedData = new float[newSampleCount * channels];
            System.Array.Copy(data, startFrame * channels, trimmedData, 0, trimmedData.Length);

            AudioClip trimmed = AudioClip.Create(clip.name + "_Trimmed", newSampleCount, channels, clip.frequency, false);
            trimmed.SetData(trimmedData, 0);
            return trimmed;
        }
        catch
        {
            return clip;
        }
    }
}
