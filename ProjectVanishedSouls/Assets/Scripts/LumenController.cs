using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerCharacter))]
public class LumenController : MonoBehaviour
{
    public enum LumenMode
    {
        WaitingAtStart, // Başlangıçta L tuşuna basılmasını bekliyor
        GoToFinal,      // L'ye basıldı: Final noktasına doğru yol alıyor (yolu gösteriyor)
        FollowAria,     // Tekrar L'ye basıldı: Geri dönüp Aria'yı takip ediyor (çimene girerse o da giriyor)
        ReachedFinal    // Final noktasına ulaştı
    }

    [Header("Lumen Durumu (L Tuşu ile Değişir)")]
    public LumenMode currentMode = LumenMode.WaitingAtStart;

    [Header("Hedefler")]
    [Tooltip("Lumen'in L tuşuna basılınca gideceği Final Noktası (Hierarchy'deki LumenFinalNoktasi)")]
    public Transform finalPoint;
    [Tooltip("Takip edilecek Aria karakteri (Otomatik bulunur)")]
    public Transform ariaTransform;

    [Header("Hareket Hızları ve Yumuşatma")]
    [Tooltip("Final noktasına giderken sakin yürüme hızı")]
    public float leadSpeed = 1.15f;
    [Tooltip("Aria'ya geri dönerken ve onu takip ederken yürüme hızı")]
    public float followSpeed = 1.55f;
    [Tooltip("Hızlanma ve yavaşlama yumuşaklığı (ani hareketi önler)")]
    public float acceleration = 2.8f;
    [Tooltip("Normalde Aria'ya ne kadar yaklaşacağı (metre)")]
    public float followStopDistance = 1.25f;
    [Tooltip("Aria çimendeyken Aria'nın yanına ne kadar sokulacağı (metre)")]
    public float bushFollowStopDistance = 0.55f;

    [Header("Görsel Model Bağlantısı")]
    public Transform visualModel;

    PlayerCharacter selfPlayer;
    PlayerCharacter ariaPlayer;
    Vector3 initialHostPos;
    Vector3 initialModelPos;
    Quaternion initialModelRot;
    Vector3 currentVelocity;
    bool visualBound;
    float baseGroundY;

    LineRenderer pathLine;
    LineRenderer finalRingLine;

    void Awake()
    {
        selfPlayer = GetComponent<PlayerCharacter>();
        if (selfPlayer == null) selfPlayer = gameObject.AddComponent<PlayerCharacter>();
        selfPlayer.characterName = "Lumen";
        selfPlayer.maxHealth = 1;
        selfPlayer.diePermanentlyOnHit = true;

        if (leadSpeed > 1.35f) leadSpeed = 1.15f;
        if (followSpeed > 1.8f) followSpeed = 1.55f;

        baseGroundY = transform.position.y;
        BindVisualModel();
        SetupPathVisuals();
    }

    void Start()
    {
        baseGroundY = transform.position.y;
        FindAriaIfNeeded();
        BindVisualModel();
        SetupPathVisuals();
    }

    public void BindVisualModel()
    {
        if (visualBound || visualModel == null) return;

        visualModel.gameObject.SetActive(true);
        foreach (var col in visualModel.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        // Görsel modelin gerçek taban konumuna Lumen kök objesini tam hizala (ilk karede sıçrama olmasın)
        var r = visualModel.GetComponent<Renderer>();
        if (r != null)
        {
            Bounds b = r.bounds;
            transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
            baseGroundY = transform.position.y;
        }

        initialHostPos = transform.position;
        initialModelPos = visualModel.position;
        initialModelRot = visualModel.rotation;
        visualBound = true;
    }

    void FindAriaIfNeeded()
    {
        if (ariaTransform == null)
        {
            var ariaCtrl = FindFirstObjectByType<AriaController>();
            if (ariaCtrl != null)
            {
                ariaTransform = ariaCtrl.transform;
                ariaPlayer = ariaCtrl.GetComponent<PlayerCharacter>();
            }
        }
        else if (ariaPlayer == null)
        {
            ariaPlayer = ariaTransform.GetComponent<PlayerCharacter>();
        }
    }

    void Update()
    {
        if (selfPlayer != null && selfPlayer.IsDead)
        {
            currentVelocity = Vector3.zero;
            if (pathLine != null) pathLine.enabled = false;
            return;
        }

        FindAriaIfNeeded();

        // L tuşu kontrolü: Her basışta Final Noktasına Git <-> Aria'yı Takip Et arasında yumuşak geçiş yapar
        var kb = Keyboard.current;
        if (kb != null && kb.lKey.wasPressedThisFrame)
        {
            ToggleLumenMode();
        }

        Vector3 desiredVelocity = Vector3.zero;

        switch (currentMode)
        {
            case LumenMode.WaitingAtStart:
                desiredVelocity = Vector3.zero;
                break;

            case LumenMode.GoToFinal:
                if (finalPoint != null)
                {
                    desiredVelocity = ComputeDesiredVelocity(finalPoint.position, leadSpeed, 0.35f, out bool reached);
                    if (reached && currentVelocity.sqrMagnitude < 0.01f)
                    {
                        currentMode = LumenMode.ReachedFinal;
                        Debug.Log("Lumen Final Noktasına ulaştı!");
                    }
                }
                break;

            case LumenMode.FollowAria:
                if (ariaTransform != null)
                {
                    bool ariaInBush = (ariaPlayer != null && ariaPlayer.isHidden) || Bush.IsPointInAnyBush(ariaTransform.position);
                    float stopDist = ariaInBush ? bushFollowStopDistance : followStopDistance;
                    desiredVelocity = ComputeDesiredVelocity(ariaTransform.position, followSpeed, stopDist, out _);
                }
                break;

            case LumenMode.ReachedFinal:
                desiredVelocity = Vector3.zero;
                break;
        }

        // Ani sıçrama veya sert dönüş olmadan hızı yumuşakça (ivmeyle) değiştir
        currentVelocity = Vector3.MoveTowards(currentVelocity, desiredVelocity, acceleration * Time.deltaTime);

        if (currentVelocity.sqrMagnitude > 0.0001f)
        {
            Vector3 nextPos = transform.position + currentVelocity * Time.deltaTime;
            nextPos.y = baseGroundY;
            // Duvarların (Cube.007 vb.) içine girmesini engelle
            nextPos = WallObstacle.PushOutOfWalls(nextPos, 0.5f);
            transform.position = nextPos;
        }

        UpdatePathVisuals();
    }

    public void ToggleLumenMode()
    {
        if (currentMode == LumenMode.GoToFinal)
        {
            currentMode = LumenMode.FollowAria;
            Debug.Log("Lumen (L): Aria'yı takip etmeye başlıyor.");
        }
        else
        {
            currentMode = LumenMode.GoToFinal;
            Debug.Log("Lumen (L): Final noktasına doğru sakin adımlarla ilerliyor.");
        }
    }

    // Aria çimendeyken Lumen de Aria'yı takip edip yanına geldiyse Lumen de çimende gizlenmiş sayılır
    public bool IsHiddenWithAria()
    {
        if (currentMode != LumenMode.FollowAria || ariaTransform == null)
            return false;

        bool ariaHidden = (ariaPlayer != null && ariaPlayer.isHidden) || Bush.IsPointInAnyBush(ariaTransform.position);
        if (!ariaHidden) return false;

        Vector3 d = ariaTransform.position - transform.position;
        d.y = 0f;
        return d.magnitude <= 2.4f;
    }

    Vector3 ComputeDesiredVelocity(Vector3 targetWorldPos, float maxSpeed, float stopDist, out bool reached)
    {
        Vector3 toGoal = targetWorldPos - transform.position;
        toGoal.y = 0f;
        float distToGoal = toGoal.magnitude;

        if (distToGoal <= stopDist)
        {
            reached = true;
            return Vector3.zero;
        }

        reached = false;

        // Önünde duvar (Cube.007 vb.) varsa etrafından dolanacak köşe noktasını al
        Vector3 steeredPos = WallObstacle.GetSteeredTarget(transform.position, targetWorldPos, 0.75f);
        Vector3 toSteered = steeredPos - transform.position;
        toSteered.y = 0f;
        Vector3 dir = toSteered.sqrMagnitude > 0.0001f ? toSteered.normalized : (toGoal / distToGoal);

        // Sadece asıl hedefe yaklaşırken son 0.8 metrede yumuşakça yavaşla (duvar köşesini dönerken duraksama yapma)
        float slowDownFactor = Mathf.Clamp01((distToGoal - stopDist) / 0.8f);
        float targetSpeed = maxSpeed * Mathf.Max(0.25f, slowDownFactor);
        return dir * targetSpeed;
    }

    void LateUpdate()
    {
        if (!visualBound || visualModel == null) return;

        // Ölüyken rotasyonu DieFallOverRoutine yönetir; hayattayken modeli döndürüp savurmadan sadece akıcı olarak kaydır
        if (selfPlayer != null && selfPlayer.IsDead)
            return;

        Vector3 worldDelta = transform.position - initialHostPos;
        visualModel.position = initialModelPos + worldDelta;
        visualModel.rotation = initialModelRot;
    }

    public void HideVisualOnDeath()
    {
        if (pathLine != null) pathLine.enabled = false;
        if (visualModel != null)
        {
            visualModel.rotation = initialModelRot;
            visualModel.gameObject.SetActive(false);
        }
    }

    // ---------- YOL ÇİZGİSİ VE FİNAL NOKTASI HALKASI ----------
    void SetupPathVisuals()
    {
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        var mat = new Material(shader);

        var existingLine = transform.Find("LumenPathLine");
        if (existingLine != null) Destroy(existingLine.gameObject);

        var lineObj = new GameObject("LumenPathLine");
        lineObj.transform.SetParent(transform, false);
        pathLine = lineObj.AddComponent<LineRenderer>();
        pathLine.useWorldSpace = true;
        pathLine.positionCount = 2;
        pathLine.startWidth = 0.08f;
        pathLine.endWidth = 0.08f;
        pathLine.material = new Material(mat);
        pathLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pathLine.enabled = false;

        if (finalPoint != null)
        {
            var existingRing = finalPoint.Find("FinalPointVisual");
            if (existingRing != null) Destroy(existingRing.gameObject);

            var ringObj = new GameObject("FinalPointVisual");
            ringObj.transform.SetParent(finalPoint, false);
            finalRingLine = ringObj.AddComponent<LineRenderer>();
            finalRingLine.useWorldSpace = true;
            finalRingLine.loop = true;
            finalRingLine.startWidth = 0.1f;
            finalRingLine.endWidth = 0.1f;
            finalRingLine.material = new Material(mat);
            finalRingLine.startColor = new Color(1f, 0.85f, 0.2f, 0.9f);
            finalRingLine.endColor = new Color(1f, 0.85f, 0.2f, 0.9f);
            finalRingLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    void UpdatePathVisuals()
    {
        if (finalPoint != null && finalRingLine != null)
        {
            int segs = 24;
            finalRingLine.positionCount = segs;
            float radius = 0.9f;
            Vector3 center = finalPoint.position + Vector3.up * 0.08f;
            for (int i = 0; i < segs; i++)
            {
                float ang = (i / (float)segs) * Mathf.PI * 2f;
                finalRingLine.SetPosition(i, center + new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius));
            }
        }

        if (pathLine == null) return;

        if (currentMode == LumenMode.GoToFinal && finalPoint != null)
        {
            pathLine.enabled = true;
            Color c = new Color(1f, 0.9f, 0.2f, 0.75f);
            pathLine.startColor = c;
            pathLine.endColor = c;
            var pts = WallObstacle.BuildDetourPath(transform.position, finalPoint.position, 0.75f);
            pathLine.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++) pathLine.SetPosition(i, pts[i]);
        }
        else if (currentMode == LumenMode.FollowAria && ariaTransform != null)
        {
            pathLine.enabled = true;
            Color c = new Color(0.3f, 0.95f, 1f, 0.65f);
            pathLine.startColor = c;
            pathLine.endColor = c;
            var pts = WallObstacle.BuildDetourPath(transform.position, ariaTransform.position, 0.75f);
            pathLine.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++) pathLine.SetPosition(i, pts[i]);
        }
        else
        {
            pathLine.enabled = false;
        }
    }

    void OnDrawGizmos()
    {
        if (finalPoint != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(finalPoint.position + Vector3.up * 0.3f, 0.8f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.2f, finalPoint.position + Vector3.up * 0.2f);
        }
    }
}
