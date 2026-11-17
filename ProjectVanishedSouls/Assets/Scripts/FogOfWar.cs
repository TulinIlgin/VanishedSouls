using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Takım üyesi (Aria, Lumen veya diğer dost karakterler).
/// Her karakterin kendi Transform'u, görüş yarıçapı ve izometrik ofseti ayrı ayarlanabilir.
/// </summary>
[System.Serializable]
public class TeamMember
{
    [Tooltip("Karakterin adı (Aria, Lumen vb.)")]
    public string name = "Karakter";

    [Tooltip("Karakterin Transform objesi")]
    public Transform character;

    [Tooltip("Bu karakterin etrafındaki dairesel görüş mesafesi (metre)")]
    public float visionRadius = 12f;

    [Tooltip("İzometrik kamera açısı için ofset (sağ/aşağı kaydırma)")]
    public Vector3 visionCenterOffset = new Vector3(1f, 0f, -1f);

    public Vector3 CenterPosition
    {
        get
        {
            if (character == null) return Vector3.zero;
            return character.position + visionCenterOffset;
        }
    }
}

/// <summary>
/// Age of Empires tarzı 3 kademeli (Siyah Keşfedilmemiş, Gri Keşfedilmiş, Tam Açık Görüş İçi)
/// 360 derece çoklu karakter (Aria + Lumen + Ekip) destekli Savaş Sisi (Fog of War) yöneticisi.
/// </summary>
public class FogOfWar : MonoBehaviour
{
    public static FogOfWar Instance { get; private set; }

    [Header("Sis Düzlemi")]
    [Tooltip("Sahnede yer alan FogPlane objesinin MeshFilter bileşeni")]
    [SerializeField] private MeshFilter fogPlaneMeshFilter;

    [Header("Görüş Açan Takım Üyeleri (Aria, Lumen vb.)")]
    [Tooltip("Sahnede sis açacak karakterlerin listesi. Aria, Lumen ve diğer dost birimleri buraya ekleyebilirsiniz.")]
    [SerializeField] private List<TeamMember> teamMembers = new List<TeamMember>();

    [Header("Başlangıç Alanı")]
    [Tooltip("Oyun başladığında ana karakterin etrafında anında açık olacak alanın yarıçapı")]
    [SerializeField] private float initialRevealRadius = 10f;

    [Header("Görünüm ve Renk Ayarları")]
    [Tooltip("Keşfedilmemiş alanın rengi")]
    [SerializeField] private Color unexploredColor = new Color(0.05f, 0.05f, 0.05f, 1f);
    [Tooltip("Keşfedilmemiş alanın opaklığı (1 = tamamen siyah)")]
    [SerializeField, Range(0f, 1f)] private float unexploredAlpha = 1f;

    [Tooltip("Keşfedilmiş ama şu an görüş dışındaki alanın rengi")]
    [SerializeField] private Color exploredColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    [Tooltip("Keşfedilmiş ama şu an görüş dışındaki gri sis opaklığı")]
    [SerializeField, Range(0f, 1f)] private float exploredFogAlpha = 0.55f;

    [Header("Geçiş Ayarları")]
    [Tooltip("Sisin açılma ve griye dönme hızı")]
    [SerializeField] private float revealSpeed = 6f;
    [Tooltip("Görüş alanının sınırındaki yumuşak geçiş mesafesi (metre)")]
    [SerializeField] private float softEdgeFeather = 2f;

    // Geriye dönük uyumluluk alanları
    [SerializeField, HideInInspector] private Transform player;
    [SerializeField, HideInInspector] private float ariaVisionRadius = 12f;
    [SerializeField, HideInInspector] private Vector3 visionCenterOffset = new Vector3(1f, 0f, -1f);

    private static readonly List<FogRevealer> activeRevealers = new List<FogRevealer>();

    private Mesh fogMesh;
    private Vector3[] worldVertices;
    private Color[] vertexColors;
    private bool[] explored;

    public List<TeamMember> TeamMembers => teamMembers;

    public static void RegisterRevealer(FogRevealer revealer)
    {
        if (revealer != null && !activeRevealers.Contains(revealer))
        {
            activeRevealers.Add(revealer);
        }
    }

    public static void UnregisterRevealer(FogRevealer revealer)
    {
        if (revealer != null)
        {
            activeRevealers.Remove(revealer);
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Eğer Inspector'da teamMembers boşsa otomatik doldur
        EnsureTeamMembersConfigured();

        if (fogPlaneMeshFilter == null)
        {
            fogPlaneMeshFilter = GetComponent<MeshFilter>();
            if (fogPlaneMeshFilter == null)
            {
                var plane = GameObject.Find("FogPlane");
                if (plane != null) fogPlaneMeshFilter = plane.GetComponent<MeshFilter>();
            }
        }

        if (fogPlaneMeshFilter == null || fogPlaneMeshFilter.sharedMesh == null)
        {
            Debug.LogError("FogOfWar: FogPlane MeshFilter veya Mesh atanmamış!", this);
            enabled = false;
            return;
        }

        // Kendi kopyasını oluştur (orijinal mesh asset'i bozulmasın)
        fogMesh = Instantiate(fogPlaneMeshFilter.sharedMesh);
        fogMesh.name = "FogMesh_Instance";
        fogPlaneMeshFilter.mesh = fogMesh;

        Vector3[] vertices = fogMesh.vertices;
        int vertexCount = vertices.Length;

        worldVertices = new Vector3[vertexCount];
        vertexColors = new Color[vertexCount];
        explored = new bool[vertexCount];

        Transform planeTransform = fogPlaneMeshFilter.transform;
        for (int i = 0; i < vertexCount; i++)
        {
            worldVertices[i] = planeTransform.TransformPoint(vertices[i]);
            Color c = unexploredColor;
            c.a = unexploredAlpha;
            vertexColors[i] = c;
        }

        // Başlangıçta ana karakterin etrafını doğrudan açık yap
        ApplyInitialReveal();

        fogMesh.colors = vertexColors;
    }

    void EnsureTeamMembersConfigured()
    {
        if (teamMembers == null) teamMembers = new List<TeamMember>();

        // Eğer liste tamamen boşsa Aria ve Lumen'i otomatik bulup ekle
        if (teamMembers.Count == 0)
        {
            var aria = FindAnyObjectByType<AriaController>();
            if (aria != null)
            {
                teamMembers.Add(new TeamMember
                {
                    name = "Aria",
                    character = aria.transform,
                    visionRadius = ariaVisionRadius > 0f ? ariaVisionRadius : 12f,
                    visionCenterOffset = visionCenterOffset
                });
            }

            var lumen = FindAnyObjectByType<LumenController>();
            if (lumen != null)
            {
                teamMembers.Add(new TeamMember
                {
                    name = "Lumen",
                    character = lumen.transform,
                    visionRadius = 22f, // Lumen'in görüş menzili Aria'dan çok daha uzundur
                    visionCenterOffset = visionCenterOffset
                });
            }
        }
    }

    private void ApplyInitialReveal()
    {
        if (teamMembers.Count == 0 || initialRevealRadius <= 0f) return;

        // Başlangıçta ilk üyenin (Aria) etrafındaki alanı aç
        Vector3 startPos = teamMembers[0].CenterPosition;
        float initRadSqr = initialRevealRadius * initialRevealRadius;

        for (int i = 0; i < worldVertices.Length; i++)
        {
            Vector3 toVertex = worldVertices[i] - startPos;
            toVertex.y = 0f;

            float distSqr = toVertex.sqrMagnitude;
            if (distSqr <= initRadSqr)
            {
                explored[i] = true;
                float dist = Mathf.Sqrt(distSqr);

                float innerRadius = Mathf.Max(0f, initialRevealRadius - softEdgeFeather);
                float targetAlpha = 0f;
                if (dist > innerRadius && softEdgeFeather > 0.01f)
                {
                    targetAlpha = Mathf.InverseLerp(innerRadius, initialRevealRadius, dist) * exploredFogAlpha;
                }

                Color c = Color.Lerp(exploredColor, unexploredColor, targetAlpha / Mathf.Max(0.01f, unexploredAlpha));
                c.a = targetAlpha;
                vertexColors[i] = c;
            }
        }
    }

    private void Update()
    {
        if (worldVertices == null) return;

        float dt = Time.deltaTime;
        float rate = dt * revealSpeed;

        int memberCount = teamMembers.Count;
        int revealerCount = activeRevealers.Count;

        for (int i = 0; i < worldVertices.Length; i++)
        {
            Vector3 vPos = worldVertices[i];
            float bestVisibilityAlpha = 1f;

            // 1. Tüm Takım Üyeleri (Aria, Lumen vb.) kontrolü
            for (int m = 0; m < memberCount; m++)
            {
                TeamMember member = teamMembers[m];
                if (member == null || member.character == null) continue;

                Vector3 mPos = member.CenterPosition;
                float dx = vPos.x - mPos.x;
                float dz = vPos.z - mPos.z;
                float distSqr = dx * dx + dz * dz;

                float rad = member.visionRadius;
                if (distSqr <= rad * rad)
                {
                    explored[i] = true;
                    float dist = Mathf.Sqrt(distSqr);
                    float inner = Mathf.Max(0f, rad - softEdgeFeather);
                    float alphaFactor = (dist <= inner || softEdgeFeather <= 0.01f)
                        ? 0f
                        : Mathf.InverseLerp(inner, rad, dist);

                    bestVisibilityAlpha = Mathf.Min(bestVisibilityAlpha, alphaFactor);
                }
            }

            // 2. Haritadaki aktif Lambalar / Işık Kaynakları kontrolü
            for (int r = 0; r < revealerCount; r++)
            {
                FogRevealer rev = activeRevealers[r];
                if (rev == null) continue;

                Vector3 revPos = rev.CenterPosition;
                float rdx = vPos.x - revPos.x;
                float rdz = vPos.z - revPos.z;
                float rDistSqr = rdx * rdx + rdz * rdz;

                float rRad = rev.Radius;
                if (rDistSqr <= rRad * rRad)
                {
                    explored[i] = true;
                    float rDist = Mathf.Sqrt(rDistSqr);
                    float rInner = Mathf.Max(0f, rRad - softEdgeFeather);
                    float rAlpha = (rDist <= rInner || softEdgeFeather <= 0.01f)
                        ? 0f
                        : Mathf.InverseLerp(rInner, rRad, rDist);

                    bestVisibilityAlpha = Mathf.Min(bestVisibilityAlpha, rAlpha);
                }
            }

            // 3. Renk ve alpha belirleme
            Color targetColor;
            float targetAlpha;

            if (bestVisibilityAlpha < 1f)
            {
                targetColor = exploredColor;
                targetAlpha = Mathf.Lerp(0f, exploredFogAlpha, bestVisibilityAlpha);
            }
            else if (explored[i])
            {
                targetColor = exploredColor;
                targetAlpha = exploredFogAlpha;
            }
            else
            {
                targetColor = unexploredColor;
                targetAlpha = unexploredAlpha;
            }

            // Pürüzsüz geçiş
            Color current = vertexColors[i];
            current.r = Mathf.MoveTowards(current.r, targetColor.r, rate);
            current.g = Mathf.MoveTowards(current.g, targetColor.g, rate);
            current.b = Mathf.MoveTowards(current.b, targetColor.b, rate);
            current.a = Mathf.MoveTowards(current.a, targetAlpha, rate);

            vertexColors[i] = current;
        }

        fogMesh.colors = vertexColors;
    }

    /// <summary>
    /// Verilen dünya pozisyonunun oyuncuların (Aria, Lumen) ya da lambaların anlık görüş alanı içinde olup olmadığını test eder.
    /// </summary>
    public bool IsPositionInVision(Vector3 worldPos)
    {
        // 1. Takım üyelerinden herhangi birinin görüş alanında mı?
        for (int i = 0; i < teamMembers.Count; i++)
        {
            TeamMember member = teamMembers[i];
            if (member == null || member.character == null) continue;

            Vector3 d = worldPos - member.CenterPosition;
            d.y = 0f;
            if (d.sqrMagnitude <= (member.visionRadius * member.visionRadius))
                return true;
        }

        // 2. Herhangi bir lambanın görüş alanında mı?
        for (int i = 0; i < activeRevealers.Count; i++)
        {
            FogRevealer rev = activeRevealers[i];
            if (rev == null) continue;

            Vector3 d = worldPos - rev.CenterPosition;
            d.y = 0f;
            if (d.sqrMagnitude <= (rev.Radius * rev.Radius))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Verilen dünya pozisyonunun daha önce keşfedilip edilmediğini test eder.
    /// </summary>
    public bool IsPositionExplored(Vector3 worldPos)
    {
        if (worldVertices == null || explored == null || worldVertices.Length == 0) return false;

        float bestDistSqr = float.MaxValue;
        int nearestIndex = -1;

        for (int i = 0; i < worldVertices.Length; i++)
        {
            float dx = worldVertices[i].x - worldPos.x;
            float dz = worldVertices[i].z - worldPos.z;
            float d2 = dx * dx + dz * dz;

            if (d2 < bestDistSqr)
            {
                bestDistSqr = d2;
                nearestIndex = i;
            }
        }

        return nearestIndex >= 0 && explored[nearestIndex];
    }

    private void OnDestroy()
    {
        if (fogMesh != null)
        {
            Destroy(fogMesh);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (teamMembers == null) return;

        Color[] colors = new Color[]
        {
            new Color(0.2f, 0.6f, 1f, 0.5f),   // 1. Üye: Mavi
            new Color(1f, 0.85f, 0.2f, 0.5f),  // 2. Üye (Lumen): Altın Sarısı
            new Color(0.2f, 1f, 0.6f, 0.5f)    // Diğerleri: Yeşil
        };

        for (int i = 0; i < teamMembers.Count; i++)
        {
            TeamMember m = teamMembers[i];
            if (m == null || m.character == null) continue;

            Gizmos.color = colors[i % colors.Length];
            Gizmos.DrawWireSphere(m.CenterPosition, m.visionRadius);
        }

        if (teamMembers.Count > 0 && teamMembers[0].character != null)
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.35f);
            Gizmos.DrawWireSphere(teamMembers[0].CenterPosition, initialRevealRadius);
        }
    }
}
