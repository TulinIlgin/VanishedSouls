using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MOBA tarzı çalıya girme, gölgelenme (kararma), eğilme (crouch) ve gizlenme mekaniği.
/// Karakterin (Aria veya oyuncu) üzerine ekleyin.
/// </summary>
public class PlayerStealth : MonoBehaviour
{
    [Header("Gizlilik Durumu")]
    [Tooltip("Karakter şu anda çimende gizli mi? (Düşman AI bu değere bakar)")]
    public bool isHidden = false;

    [Header("Gölge / Küllü Renk Ayarları")]
    [Tooltip("Çimene girince orijinal rengin parlaklığı (0.4: gölgeli koyu ton)")]
    [Range(0.1f, 1f)] public float shadowBrightness = 0.45f;
    [Tooltip("Küllü/gölgeli hava katmak için hafif soğuk gri ton")]
    public Color shadowTint = new Color(0.35f, 0.38f, 0.45f, 1f);
    [Tooltip("Rengin kararma ve açılma geçiş hızı")]
    public float colorTransitionSpeed = 7f;

    [Header("Referanslar (Boşsa otomatik bulunur)")]
    public Animator anim;
    public Renderer[] characterRenderers;

    PlayerCharacter playerChar;
    readonly Dictionary<Material, Color> originalColors = new Dictionary<Material, Color>();
    readonly List<Material> cachedMaterials = new List<Material>();
    int grassTriggerCount = 0;
    Coroutine colorFadeRoutine;

    void Awake()
    {
        playerChar = GetComponent<PlayerCharacter>();
        if (anim == null) anim = GetComponentInChildren<Animator>();

        // Unity'de OnTriggerEnter çalışması için karakterde mutlaka bir Rigidbody olmalıdır!
        var rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        CacheCharacterMaterials();
    }

    void CacheCharacterMaterials()
    {
        if (characterRenderers == null || characterRenderers.Length == 0)
            characterRenderers = GetComponentsInChildren<Renderer>(true);

        originalColors.Clear();
        cachedMaterials.Clear();

        foreach (var r in characterRenderers)
        {
            if (r == null || !r.enabled) continue;
            // Bıçak veya silah renderers'ını hariç tut
            if (r.name.ToLower().Contains("knife") || r.name.ToLower().Contains("bicak")) continue;

            foreach (var mat in r.materials)
            {
                if (mat == null) continue;
                string prop = GetColorPropertyName(mat);
                if (prop != null && !originalColors.ContainsKey(mat))
                {
                    originalColors[mat] = mat.GetColor(prop);
                    cachedMaterials.Add(mat);
                }
            }
        }
    }

    string GetColorPropertyName(Material mat)
    {
        if (mat.HasProperty("_BaseColor")) return "_BaseColor";
        if (mat.HasProperty("_Color")) return "_Color";
        if (mat.HasProperty("_MainColor")) return "_MainColor";
        return null;
    }

    bool IsGrassObject(Collider other)
    {
        if (other == null) return false;
        if (other.CompareTag("Grass")) return true;

        string n = other.name.ToLower();
        if (n.Contains("grass") || n.Contains("cimen") || n.Contains("çimen") || n.Contains("bush") || n.Contains("sarmasik"))
            return true;

        if (other.GetComponentInParent<Bush>() != null)
            return true;

        return false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsGrassObject(other))
        {
            grassTriggerCount++;
            var b = other.GetComponentInParent<Bush>();
            if (b != null) b.SetBushVision(true);

            if (!isHidden)
            {
                SetStealthState(true);
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (IsGrassObject(other))
        {
            grassTriggerCount = Mathf.Max(0, grassTriggerCount - 1);
            var b = other.GetComponentInParent<Bush>();
            if (b != null && grassTriggerCount == 0) b.SetBushVision(false);

            if (grassTriggerCount == 0 && isHidden)
            {
                SetStealthState(false);
            }
        }
    }

    void Update()
    {
        // Hibrit kontrol: Hem Trigger sayacını hem de haritadaki Bush pozisyonunu kontrol et
        bool inGrass = grassTriggerCount > 0 || Bush.IsPointInAnyBush(transform.position);
        if (inGrass != isHidden)
        {
            SetStealthState(inGrass);
        }

        // Çalının MOBA görüş görselini güncelle (içindeyken yarı saydamlaşır ve hafif aydınlanır)
        var closestBush = Bush.GetClosestBush(transform.position);
        if (closestBush != null)
        {
            closestBush.SetBushVision(inGrass);
        }
    }

    public void SetStealthState(bool stealth)
    {
        if (isHidden == stealth) return;
        isHidden = stealth;

        // 1. Düşman yapay zekasının (EnemyAI) kontrol ettiği PlayerCharacter'a bildir
        if (playerChar != null)
        {
            playerChar.isHidden = stealth;
        }

        // 2. Animator'da 'isCrouching' parametresini güncelle (Eğilme / Normal Duruş)
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            foreach (var p in anim.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Bool &&
                    string.Equals(p.name, "isCrouching", System.StringComparison.OrdinalIgnoreCase))
                {
                    anim.SetBool(p.name, stealth);
                    break;
                }
            }
        }

        // 3. Karakterin rengini gölgeli/küllü tona yumuşakça geçir veya orijinaline döndür
        if (cachedMaterials.Count == 0) CacheCharacterMaterials();
        if (colorFadeRoutine != null) StopCoroutine(colorFadeRoutine);
        colorFadeRoutine = StartCoroutine(TransitionColorRoutine(stealth));

        Debug.Log($"[Stealth] Karakter çalıya {(stealth ? "GİRDİ (Gizlendi / Karardı / Eğildi)" : "ÇIKTI (Görünür / Orijinal Renk / Normal Duruş)")}");
    }

    IEnumerator TransitionColorRoutine(bool toShadow)
    {
        float progress = 0f;
        while (progress < 1f)
        {
            progress += Time.deltaTime * colorTransitionSpeed;
            for (int i = 0; i < cachedMaterials.Count; i++)
            {
                var mat = cachedMaterials[i];
                if (mat == null) continue;
                string prop = GetColorPropertyName(mat);
                if (prop == null) continue;

                Color orig = originalColors[mat];
                Color target = toShadow ? (orig * shadowBrightness * shadowTint * 2.2f) : orig;
                Color current = mat.GetColor(prop);

                mat.SetColor(prop, Color.Lerp(current, target, progress));
            }
            yield return null;
        }
    }
}
