using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Düşmanlara veya sis altında gizlenmesi gereken hareketli birimlere/objelere eklenir.
/// Karakterlerin veya lambaların görüş alanı dışındayken objenin görsellerini (Renderer)
/// ve tıklanabilirlik/hedefleme bileşenlerini (Collider) otomatik olarak kapatır.
/// Lumen'in görüşü veya yeteneği ile sis altındayken Kırmızı Çizgili Siluet (Red Silhouette)
/// olarak parıldayarak işaretlenir.
/// </summary>
public class FogVisibility : MonoBehaviour
{
    [Header("Gizlenecek Bileşenler (Boşsa otomatik bulunur)")]
    [SerializeField] private List<Renderer> targetRenderers = new List<Renderer>();
    [SerializeField] private List<Collider> targetColliders = new List<Collider>();

    [Header("Yükseklik Ofseti")]
    [Tooltip("Görüş kontrolü yapılırken zemin hiyerarşisi için kullanılacak dikey ofset (metre)")]
    [SerializeField] private float heightOffset = 0.5f;

    [Header("Kırmızı Çizgili Siluet Materyali")]
    [Tooltip("Boş bırakılırsa Custom/RedSilhouette shader'ı ile otomatik oluşturulur")]
    [SerializeField] private Material redSilhouetteMaterial;

    private readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();

    private bool isVisible = true;
    private bool isShowingSilhouette = false;
    private float checkTimer;
    private const float CheckInterval = 0.03f;

    private float highlightRemaining = 0f;
    private bool isDetectedByLumen = false;
    private GameObject highlightMarker;

    public bool IsHighlighted => highlightRemaining > 0f || isDetectedByLumen;

    public void SetDetectedByLumen(bool detected)
    {
        if (isDetectedByLumen != detected)
        {
            isDetectedByLumen = detected;
            UpdateVisibility(true);
        }
    }

    private void Awake()
    {
        SetupSilhouetteMaterial();
        CacheComponents();
        SetupHighlightMarker();
    }

    void SetupSilhouetteMaterial()
    {
        if (redSilhouetteMaterial == null)
        {
            var shader = Shader.Find("Custom/RedSilhouette") ?? Shader.Find("Sprites/Default");
            redSilhouetteMaterial = new Material(shader);
            redSilhouetteMaterial.name = "Enemy_RedSilhouette_Runtime";
        }
    }

    public void CacheComponents()
    {
        targetRenderers.Clear();
        targetColliders.Clear();
        originalMaterials.Clear();

        // 1. Kendi alt objelerindeki tüm Renderer'lar
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.GetComponent<FogOfWar>() != null) continue;
            if (highlightMarker != null && r.transform.IsChildOf(highlightMarker.transform)) continue;
            
            // Kamera açısına göre kaybolmayı önlemek için Offscreen Culling'i kapat
            if (r is SkinnedMeshRenderer smr)
            {
                smr.updateWhenOffscreen = true;
            }

            if (!targetRenderers.Contains(r))
            {
                targetRenderers.Add(r);
                originalMaterials[r] = (Material[])r.sharedMaterials.Clone();
            }
        }

        // 2. Kendi alt objelerindeki tüm Collider'lar
        foreach (var c in GetComponentsInChildren<Collider>(true))
        {
            if (c.isTrigger) continue;
            if (!targetColliders.Contains(c)) targetColliders.Add(c);
        }

        // 3. EnemyAI bileşeni varsa harici VisualModel ve ekstra parçaları da dahil et
        var ai = GetComponent<EnemyAI>();
        if (ai != null)
        {
            if (ai.visualModel != null && ai.visualModel != transform)
            {
                foreach (var r in ai.visualModel.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;

                    if (!targetRenderers.Contains(r))
                    {
                        targetRenderers.Add(r);
                        originalMaterials[r] = (Material[])r.sharedMaterials.Clone();
                    }
                }
                foreach (var c in ai.visualModel.GetComponentsInChildren<Collider>(true))
                {
                    if (c.isTrigger) continue;
                    if (!targetColliders.Contains(c)) targetColliders.Add(c);
                }
            }

            if (ai.extraVisualParts != null)
            {
                foreach (var part in ai.extraVisualParts)
                {
                    if (part == null || part == transform) continue;
                    foreach (var r in part.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;

                        if (!targetRenderers.Contains(r))
                        {
                            targetRenderers.Add(r);
                            originalMaterials[r] = (Material[])r.sharedMaterials.Clone();
                        }
                    }
                    foreach (var c in part.GetComponentsInChildren<Collider>(true))
                    {
                        if (c.isTrigger) continue;
                        if (!targetColliders.Contains(c)) targetColliders.Add(c);
                    }
                }
            }
        }
    }

    void SetupHighlightMarker()
    {
        if (highlightMarker != null) return;

        var existing = transform.Find("__ScoutHighlightMarker");
        if (existing != null)
        {
            highlightMarker = existing.gameObject;
            highlightMarker.SetActive(false);
            return;
        }

        highlightMarker = new GameObject("__ScoutHighlightMarker");
        highlightMarker.transform.SetParent(transform, false);
        highlightMarker.transform.localPosition = new Vector3(0f, 2.3f, 0f);

        var line = highlightMarker.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 20;
        line.startWidth = 0.09f;
        line.endWidth = 0.09f;

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        line.material = new Material(shader);
        Color redCol = new Color(1f, 0.15f, 0.15f, 0.95f);
        line.startColor = redCol;
        line.endColor = redCol;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        float radius = 0.45f;
        for (int i = 0; i < 20; i++)
        {
            float ang = (i / 20f) * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius));
        }

        highlightMarker.SetActive(false);
    }

    public void SetHighlighted(float duration)
    {
        highlightRemaining = Mathf.Max(highlightRemaining, duration);
        if (highlightMarker != null) highlightMarker.SetActive(true);
        UpdateVisibility(true);
    }

    private void Start()
    {
        UpdateVisibility(true);
    }

    private void Update()
    {
        if (highlightRemaining > 0f)
        {
            highlightRemaining -= Time.deltaTime;
            if (highlightRemaining <= 0f)
            {
                highlightRemaining = 0f;
                if (highlightMarker != null) highlightMarker.SetActive(false);
                UpdateVisibility(true);
            }
        }

        checkTimer -= Time.deltaTime;
        if (checkTimer <= 0f)
        {
            checkTimer = CheckInterval;
            UpdateVisibility(false);
        }
    }

    private void UpdateVisibility(bool force)
    {
        if (FogOfWar.Instance == null) return;

        Vector3 checkPos = transform.position + Vector3.up * heightOffset;
        bool inVision = FogOfWar.Instance.IsPositionInVision(checkPos);

        bool shouldBeVisible = inVision || IsHighlighted;
        bool shouldShowSilhouette = !inVision && IsHighlighted;

        if (shouldBeVisible != isVisible || shouldShowSilhouette != isShowingSilhouette || force)
        {
            isVisible = shouldBeVisible;
            isShowingSilhouette = shouldShowSilhouette;
            ApplyVisibility(isVisible, inVision, isShowingSilhouette);
        }
    }

    private void ApplyVisibility(bool showVisuals, bool allowInteractions, bool useRedSilhouette)
    {
        for (int i = 0; i < targetRenderers.Count; i++)
        {
            Renderer r = targetRenderers[i];
            if (r == null) continue;

            r.enabled = showVisuals;

            if (showVisuals)
            {
                if (useRedSilhouette && redSilhouetteMaterial != null)
                {
                    int count = r.sharedMaterials.Length;
                    Material[] silhouetteMats = new Material[Mathf.Max(1, count)];
                    for (int m = 0; m < silhouetteMats.Length; m++)
                        silhouetteMats[m] = redSilhouetteMaterial;

                    r.sharedMaterials = silhouetteMats;
                }
                else if (originalMaterials.TryGetValue(r, out Material[] origMats) && origMats != null)
                {
                    r.sharedMaterials = origMats;
                }
            }
        }

        for (int i = 0; i < targetColliders.Count; i++)
        {
            Collider c = targetColliders[i];
            if (c != null)
            {
                c.enabled = allowInteractions;
            }
        }

        if (highlightMarker != null)
        {
            highlightMarker.SetActive(IsHighlighted);
        }
    }

    private void OnDisable()
    {
        ApplyVisibility(true, true, false);
    }
}
