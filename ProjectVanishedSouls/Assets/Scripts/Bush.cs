using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Çimen / çalı gizlenme alanı.
public class Bush : MonoBehaviour
{
    public static readonly List<Bush> AllBushes = new List<Bush>();

    // Düşmanın hangi karakteri çalıya girmeden önce (0.1sn hata payı sonrasında) gördüğünü tutar
    static readonly HashSet<(EnemyAI enemy, PlayerCharacter player)> spottedBeforeBushPairs = new HashSet<(EnemyAI, PlayerCharacter)>();

    const int GrassLayer = 2;

    [Tooltip("Çimenlerin arasındaki boşlukları da kapsama yarıçapı (metre)")]
    public float stealthRadius = 1.65f;

    [Tooltip("Düşmanın bizi gördükten sonra öldürme/kovalama moduna geçmesi için hata payı süresi (saniye)")]
    public const float DetectionGraceTime = 0.1f;

    [Header("MOBA Çalı Görseli (İç Görüş Açılınca)")]
    [Tooltip("Karakter içerideyken çalının yarı saydamlık (alpha) değeri")]
    [Range(0.1f, 1f)] public float insideAlpha = 0.5f;
    [Tooltip("İçerideyken yaprakların aydınlanma/vurgu çarpanı")]
    public float insideBrightness = 1.3f;
    public float fadeSpeed = 6f;

    readonly Dictionary<Material, Color> originalBushColors = new Dictionary<Material, Color>();
    readonly List<Material> bushMaterials = new List<Material>();
    Coroutine bushFadeRoutine;
    bool isVisionActive;

    static bool collidersConfigured;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        AllBushes.Clear();
        spottedBeforeBushPairs.Clear();
        collidersConfigured = false;
    }

    void OnEnable()
    {
        if (!AllBushes.Contains(this))
            AllBushes.Add(this);
        SetupGrassColliders();
        CacheBushMaterials();
    }

    void OnDisable()
    {
        AllBushes.Remove(this);
    }

    void Start()
    {
        SetupGrassColliders();
        CacheBushMaterials();
    }

    public static Bush GetClosestBush(Vector3 worldPos)
    {
        Bush closest = null;
        float minDist = float.MaxValue;
        for (int i = 0; i < AllBushes.Count; i++)
        {
            var b = AllBushes[i];
            if (b == null) continue;
            float d = Vector3.Distance(b.transform.position, worldPos);
            if (d < minDist)
            {
                minDist = d;
                closest = b;
            }
        }
        return closest;
    }

    public void CacheBushMaterials()
    {
        if (originalBushColors.Count > 0) return;

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled) continue;
            foreach (var mat in r.materials)
            {
                if (mat == null) continue;
                string prop = mat.HasProperty("_BaseColor") ? "_BaseColor" : (mat.HasProperty("_Color") ? "_Color" : null);
                if (prop != null && !originalBushColors.ContainsKey(mat))
                {
                    originalBushColors[mat] = mat.GetColor(prop);
                    bushMaterials.Add(mat);
                }
            }
        }
    }

    public void SetBushVision(bool active)
    {
        if (isVisionActive == active) return;
        isVisionActive = active;

        CacheBushMaterials();
        if (bushFadeRoutine != null) StopCoroutine(bushFadeRoutine);
        bushFadeRoutine = StartCoroutine(FadeBushRoutine(active));
    }

    IEnumerator FadeBushRoutine(bool active)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * fadeSpeed;
            for (int i = 0; i < bushMaterials.Count; i++)
            {
                var mat = bushMaterials[i];
                if (mat == null) continue;
                string prop = mat.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                Color orig = originalBushColors[mat];

                // Karakter içerideyken: Yapraklar hafif aydınlanır + yarı saydamlaşır (MOBA çalı görüşü)
                Color target = active
                    ? new Color(orig.r * insideBrightness, orig.g * insideBrightness, orig.b * insideBrightness, insideAlpha)
                    : orig;

                Color cur = mat.GetColor(prop);
                mat.SetColor(prop, Color.Lerp(cur, target, t));
            }
            yield return null;
        }
    }

    public static void MarkSpottedBeforeBush(EnemyAI enemy, PlayerCharacter player)
    {
        if (enemy == null || player == null) return;
        spottedBeforeBushPairs.Add((enemy, player));
    }

    public static void ClearSpottedState(EnemyAI enemy, PlayerCharacter player = null)
    {
        if (enemy == null) return;
        if (player != null)
        {
            spottedBeforeBushPairs.Remove((enemy, player));
            return;
        }

        spottedBeforeBushPairs.RemoveWhere(pair => pair.enemy == enemy);
    }

    public static bool WasSpottedBeforeEnteringBush(EnemyAI enemy, PlayerCharacter player)
    {
        if (enemy == null || player == null) return false;
        return spottedBeforeBushPairs.Contains((enemy, player));
    }

    public static bool IsHiddenFromEnemy(PlayerCharacter player, EnemyAI enemy)
    {
        if (player == null) return false;
        bool physicallyInBush = player.isHidden || IsPointInAnyBush(player.WorldPosition);
        if (!physicallyInBush) return false;

        if (enemy != null && WasSpottedBeforeEnteringBush(enemy, player))
            return false;

        return true;
    }

    public void SetupGrassColliders()
    {
        Physics.IgnoreLayerCollision(GrassLayer, 0, true);
        Physics.IgnoreLayerCollision(GrassLayer, 8, true);

        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf == null || mf.sharedMesh == null) continue;
            if (mf.gameObject.name == "Object_4" || mf.gameObject.name == "Object_4.002") continue;

            mf.gameObject.layer = GrassLayer;
            var mc = mf.GetComponent<MeshCollider>();
            if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.enabled = true;
        }
        collidersConfigured = true;
    }

    public static bool IsPointInAnyBush(Vector3 worldPos)
    {
        if (!collidersConfigured)
        {
            for (int i = 0; i < AllBushes.Count; i++)
            {
                if (AllBushes[i] != null) AllBushes[i].SetupGrassColliders();
            }
            collidersConfigured = true;
        }

        int grassMask = 1 << GrassLayer;
        bool prevBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;

        bool hit = false;
        if (Physics.CheckSphere(worldPos + Vector3.up * 0.6f, 1.65f, grassMask, QueryTriggerInteraction.Collide))
        {
            hit = true;
        }
        else if (Physics.SphereCast(worldPos + Vector3.up * 4.0f, 1.65f, Vector3.down, out _, 6.5f, grassMask, QueryTriggerInteraction.Collide))
        {
            hit = true;
        }

        Physics.queriesHitBackfaces = prevBackfaces;
        return hit;
    }
}
