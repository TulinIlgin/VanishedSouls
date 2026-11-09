using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lumen Karakter Kontrolcüsü.
/// Artık direkt olarak herhangi bir yeni karakter objesinin üzerine (Component olarak) eklenebilir.
/// </summary>
[RequireComponent(typeof(PlayerCharacter))]
public class LumenController : MonoBehaviour
{
    public enum LumenMode
    {
        WaitingAtStart, // Başlangıçta L tuşuna basılmasını bekliyor
        GoToFinal,      // L'ye basıldı: Final noktasına doğru yol alıyor (yolu gösteriyor)
        FollowAria,     // Tekrar L'ye basıldı: Geri dönüp Aria'yı takip ediyor
        ReachedFinal    // Final noktasına ulaştı
    }

    [Header("Lumen Durumu (L Tuşu ile Değişir)")]
    public LumenMode currentMode = LumenMode.WaitingAtStart;

    [Header("Hedefler (Boşsa otomatik bulunur)")]
    [Tooltip("Lumen'in L tuşuna basılınca gideceği Final Noktası")]
    public Transform finalPoint;
    [Tooltip("Takip edilecek Aria karakteri")]
    public Transform ariaTransform;

    [Header("Hareket Hızları")]
    [Tooltip("Final noktasına giderken sakin yürüme hızı")]
    public float leadSpeed = 1.15f;
    [Tooltip("Aria'ya geri dönerken ve onu takip ederken yürüme hızı")]
    public float followSpeed = 1.55f;
    [Tooltip("Hızlanma ve yavaşlama yumuşaklığı")]
    public float acceleration = 2.8f;
    [Tooltip("Normalde Aria'ya ne kadar yaklaşacağı (metre)")]
    public float followStopDistance = 1.25f;
    [Tooltip("Aria çimendeyken Aria'nın yanına ne kadar sokulacağı (metre)")]
    public float bushFollowStopDistance = 0.55f;

    [Header("Görsel & Animasyon")]
    [Tooltip("Görsel model (Boş bırakılırsa bu objenin kendisi kullanılır)")]
    public Transform visualModel;
    [Tooltip("Yürüdüğü yöne doğru yumuşakça dönsün mü?")]
    public bool faceMovementDirection = true;
    public float rotationSpeed = 10f;

    PlayerCharacter selfPlayer;
    PlayerCharacter ariaPlayer;
    Animator anim;
    Vector3 currentVelocity;
    float baseGroundY;

    LineRenderer pathLine;
    LineRenderer finalRingLine;

    void Awake()
    {
        // 1. Karakter Katmanı (Layer: Character - 6)
        int charLayer = LayerMask.NameToLayer("Character");
        if (charLayer < 0) charLayer = 6;
        SetLayerRecursively(transform, charLayer);

        // 2. PlayerCharacter yapılandırması
        selfPlayer = GetComponent<PlayerCharacter>();
        if (selfPlayer == null) selfPlayer = gameObject.AddComponent<PlayerCharacter>();
        selfPlayer.characterName = "Lumen";
        selfPlayer.maxHealth = 1;
        selfPlayer.diePermanentlyOnHit = true;

        // 3. Fizik ve Çalı Algılama (Collider + Kinematic Rigidbody)
        var col = GetComponent<Collider>();
        if (col == null)
        {
            var cap = gameObject.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 0.9f, 0f);
            cap.height = 1.8f;
            cap.radius = 0.35f;
        }

        var rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // 4. Görsel Model ve Animator
        if (visualModel == null)
            visualModel = transform;

        anim = GetComponentInChildren<Animator>();
        baseGroundY = transform.position.y;

        SetupPathVisuals();
    }

    void Start()
    {
        baseGroundY = transform.position.y;
        FindAriaIfNeeded();
        FindFinalPointIfNeeded();
        SetupPathVisuals();
    }

    void FindFinalPointIfNeeded()
    {
        if (finalPoint == null)
        {
            var finalObj = GameObject.Find("LumenFinalNoktasi");
            if (finalObj != null)
            {
                finalPoint = finalObj.transform;
            }
            else
            {
                var created = new GameObject("LumenFinalNoktasi");
                created.transform.position = new Vector3(-23.5f, baseGroundY, 17.5f);
                finalPoint = created.transform;
            }
        }

        if (finalPoint != null)
        {
            FinishLine.EnsureOnFinalPoint(finalPoint);
        }
    }

    void FindAriaIfNeeded()
    {
        if (ariaTransform == null)
        {
            var ariaCtrl = FindAnyObjectByType<AriaController>();
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
        if (GameMenuManager.IsMenuOpen) return;

        if (selfPlayer != null && selfPlayer.IsDead)
        {
            currentVelocity = Vector3.zero;
            if (pathLine != null) pathLine.enabled = false;
            return;
        }

        FindAriaIfNeeded();

        // L tuşu kontrolü: Final Noktası <-> Aria'yı Takip Et
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

        // Hızı yumuşakça (ivmeyle) değiştir
        currentVelocity = Vector3.MoveTowards(currentVelocity, desiredVelocity, acceleration * Time.deltaTime);

        // Karakteri doğrudan hareket ettir
        if (currentVelocity.sqrMagnitude > 0.0001f)
        {
            Vector3 nextPos = transform.position + currentVelocity * Time.deltaTime;
            nextPos.y = baseGroundY;
            nextPos = WallObstacle.PushOutOfWalls(nextPos, 0.45f);
            transform.position = nextPos;

            // Yürüdüğü yöne doğru dön
            if (faceMovementDirection)
            {
                Vector3 flatDir = new Vector3(currentVelocity.x, 0f, currentVelocity.z);
                if (flatDir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(flatDir, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
                }
            }
        }

        UpdateAnimator();
        UpdatePathVisuals();
    }

    void UpdateAnimator()
    {
        if (anim == null)
            anim = GetComponentInChildren<Animator>();

        if (anim == null) return;

        bool isMoving = currentVelocity.sqrMagnitude > 0.04f;
        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool &&
                (string.Equals(p.name, "IsWalking", System.StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(p.name, "isMoving", System.StringComparison.OrdinalIgnoreCase)))
            {
                anim.SetBool(p.name, isMoving);
            }
            else if (p.type == AnimatorControllerParameterType.Float &&
                     string.Equals(p.name, "Speed", System.StringComparison.OrdinalIgnoreCase))
            {
                anim.SetFloat(p.name, currentVelocity.magnitude);
            }
        }
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

    /// <summary>
    /// Aria çimendeyken veya gizliyken Lumen de Aria'nın yanındaysa gizli sayılır!
    /// (Mod fark etmeksizin başlangıçta da korunur).
    /// </summary>
    public bool IsHiddenWithAria()
    {
        FindAriaIfNeeded();
        if (ariaTransform == null) return false;

        bool ariaHidden = (ariaPlayer != null && ariaPlayer.isHidden) || Bush.IsPointInAnyBush(ariaTransform.position);
        if (!ariaHidden) return false;

        Vector3 d = ariaTransform.position - transform.position;
        d.y = 0f;
        return d.magnitude <= 3.0f;
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

        Vector3 steeredPos = WallObstacle.GetSteeredTarget(transform.position, targetWorldPos, 0.75f);
        Vector3 toSteered = steeredPos - transform.position;
        toSteered.y = 0f;
        Vector3 dir = toSteered.sqrMagnitude > 0.0001f ? toSteered.normalized : (toGoal / distToGoal);

        float slowDownFactor = Mathf.Clamp01((distToGoal - stopDist) / 0.8f);
        float targetSpeed = maxSpeed * Mathf.Max(0.25f, slowDownFactor);
        return dir * targetSpeed;
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null || layer < 0) return;
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
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
