using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lumen Karakter Kontrolcüsü.
/// - Tuşa kısa basış: Final Noktası <-> Aria'yı Takip Et geçişi yapar.
/// - Tuşa 0.5s basılı tutup çekme: Olduğu yerde SABİT DURUR (Bekle modu).
/// - Tekrar kısa basış: Harekete devam eder.
/// - Hareket esnasında öne ve hafif çapraza doğru süzülerek eğilme (Lean/Tilt) efekti.
/// - Savaş sisi açma yarıçapı ve kırmızı çizgili düşman algılama ayarları.
/// </summary>
[RequireComponent(typeof(PlayerCharacter))]
public class LumenController : MonoBehaviour
{
    public enum LumenMode
    {
        WaitingAtStart, // Başlangıçta tuşa basılmasını bekliyor
        GoToFinal,      // 1. Tık: Final noktasına doğru sakin adımlarla yol alıyor
        FollowAria,     // 2. Tık: Geri dönüp Aria'yı takip ediyor
        StayHere,       // 0.5s basılı tutuldu: Olduğu yerde sabit duruyor
        ReachedFinal    // Final noktasına ulaştı
    }

    [Header("Lumen Durumu")]
    public LumenMode currentMode = LumenMode.WaitingAtStart;

    [Header("Tuş Kontrolleri")]
    [Tooltip("Lumen ana komut tuşu (Varsayılan: L)")]
    public Key primaryKey = Key.L;
    [Tooltip("Alternatif komut tuşu (İsteğe bağlı: D vb.)")]
    public Key secondaryKey = Key.D;
    [Tooltip("Sabit dur modu için gereken basılı tutma süresi (saniye)")]
    public float holdThreshold = 0.5f;

    [Header("Hedefler (Boşsa otomatik bulunur)")]
    [Tooltip("Lumen'in gideceği Final Noktası")]
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

    [Header("Öne ve Çapraza Eğilme Efekti (Lean / Tilt)")]
    [Tooltip("Hareket esnasında öne doğru eğilme açısı (derece)")]
    public float forwardLeanAngle = 14f;
    [Tooltip("Hafif çapraz yana yatma açısı (derece)")]
    public float sideTiltAngle = 4f;
    [Tooltip("Eğilme ve düzelme yumuşaklık hızı")]
    public float leanSmoothSpeed = 8f;

    [Header("Görsel & Dönüş Ayarları")]
    [Tooltip("Görsel model (Boşsa altındaki model objesi otomatik bulunur)")]
    public Transform visualModel;
    [Tooltip("Yürüdüğü yöne doğru yumuşakça dönsün mü?")]
    public bool faceMovementDirection = true;
    public float rotationSpeed = 10f;

    [Header("Savaş Sisi ve Görüş Ayarları (Fog of War)")]
    [Tooltip("Lumen'in fiziksel olarak etrafındaki sisi ne kadar açacağı / yaracağı (metre)")]
    public float fogVisionRadius = 6f;

    [Tooltip("Düşmanları sis altında ne kadar uzaktan kırmızı çizgili göreceği (metre)")]
    public float redSilhouetteDetectionRadius = 25f;

    PlayerCharacter selfPlayer;
    PlayerCharacter ariaPlayer;
    Animator anim;
    Vector3 currentVelocity;
    float baseGroundY;

    LineRenderer pathLine;
    LineRenderer finalRingLine;
    float scanTimer;
    readonly HashSet<FogVisibility> detectedEnemies = new HashSet<FogVisibility>();

    float keyPressStartTime;
    bool isKeyPressed;
    bool hasTriggeredHold;
    float currentLean;
    float currentSideTilt;

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

        // 4. Görsel Model tespiti
        if (visualModel == null)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("LumenPath") || child.name.StartsWith("FinalPoint")) continue;
                if (child.GetComponent<Renderer>() != null || child.GetComponent<Animator>() != null)
                {
                    visualModel = child;
                    break;
                }
            }
            if (visualModel == null) visualModel = transform;
        }

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

        // Tuş girişi (Kısa basış: Mod değiştir / 0.5s basılı tutma: Sabit dur)
        HandleKeyInput();

        Vector3 desiredVelocity = Vector3.zero;

        switch (currentMode)
        {
            case LumenMode.WaitingAtStart:
            case LumenMode.StayHere:
            case LumenMode.ReachedFinal:
                desiredVelocity = Vector3.zero;
                break;

            case LumenMode.GoToFinal:
                if (finalPoint != null)
                {
                    desiredVelocity = ComputeDesiredVelocity(finalPoint.position, leadSpeed, 0.35f, out bool reached);
                    if (reached && currentVelocity.sqrMagnitude < 0.01f)
                    {
                        currentMode = LumenMode.ReachedFinal;
                        Debug.Log("Lumen: Final Noktasına ulaştı!");
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
        }

        // Hızı ivmeyle yumuşakça değiştir
        currentVelocity = Vector3.MoveTowards(currentVelocity, desiredVelocity, acceleration * Time.deltaTime);

        // Karakteri hareket ettir
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

        // Öne doğru hafif bükülme / çapraz eğilme (Lean/Tilt) efekti
        UpdateLeanTilt();

        UpdateAnimator();
        UpdatePathVisuals();
        UpdateFogDetection();
    }

    void HandleKeyInput()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        bool isDown = kb[primaryKey].isPressed || (secondaryKey != Key.None && kb[secondaryKey].isPressed);
        bool wasPressed = kb[primaryKey].wasPressedThisFrame || (secondaryKey != Key.None && kb[secondaryKey].wasPressedThisFrame);
        bool wasReleased = kb[primaryKey].wasReleasedThisFrame || (secondaryKey != Key.None && kb[secondaryKey].wasReleasedThisFrame);

        // 1. Tuşa basıldığı an
        if (wasPressed)
        {
            keyPressStartTime = Time.time;
            isKeyPressed = true;
            hasTriggeredHold = false;
        }

        // 2. Tuş basılı tutulurken: 0.5 saniye DOLDUĞU AN (parmağı çekmeyi beklemeden) otomatik dur!
        if (isKeyPressed && isDown && !hasTriggeredHold)
        {
            if (Time.time - keyPressStartTime >= holdThreshold)
            {
                hasTriggeredHold = true;
                currentMode = LumenMode.StayHere;
                Debug.Log("Lumen: Tuş 0.5s basılı tutuldu -> Olduğu yerde SABİT DURUYOR (Bekleme Modu).");
            }
        }

        // 3. Tuş bırakıldığında: Eğer 0.5s dolmadan önce bırakıldıysa (Kısa Tıklama)
        if (wasReleased)
        {
            if (isKeyPressed && !hasTriggeredHold)
            {
                // Kısa basış -> Normal geçiş (Final <-> Takip)
                ToggleLumenMode();
            }
            isKeyPressed = false;
            hasTriggeredHold = false;
        }
    }

    public void ToggleLumenMode()
    {
        if (currentMode == LumenMode.GoToFinal)
        {
            currentMode = LumenMode.FollowAria;
            Debug.Log("Lumen: 2. Basış -> Aria'yı takip etmeye başlıyor.");
        }
        else
        {
            currentMode = LumenMode.GoToFinal;
            Debug.Log("Lumen: 1. Basış -> Final noktasına doğru sakin adımlarla ilerliyor.");
        }
    }

    /// <summary>
    /// Hareket hızına bağlı olarak Lumen'in öne ve hafif çapraza doğru süzülerek bükülmesini sağlar.
    /// </summary>
    void UpdateLeanTilt()
    {
        float speedRatio = Mathf.Clamp01(currentVelocity.magnitude / Mathf.Max(0.5f, followSpeed));
        float targetLean = speedRatio * forwardLeanAngle;
        float targetSide = speedRatio * sideTiltAngle;

        currentLean = Mathf.Lerp(currentLean, targetLean, leanSmoothSpeed * Time.deltaTime);
        currentSideTilt = Mathf.Lerp(currentSideTilt, targetSide, leanSmoothSpeed * Time.deltaTime);

        Quaternion tilt = Quaternion.Euler(currentLean, 0f, -currentSideTilt);

        if (visualModel != null && visualModel != transform)
        {
            visualModel.localRotation = tilt;
        }
        else
        {
            // Eğer model alt obje değilse ana transform rotasyonuna yumuşakça uygula
            Vector3 flatDir = currentVelocity.sqrMagnitude > 0.001f ? currentVelocity.normalized : transform.forward;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude > 0.001f)
            {
                Quaternion faceRot = Quaternion.LookRotation(flatDir, Vector3.up);
                transform.rotation = faceRot * tilt;
            }
        }
    }

    void UpdateFogDetection()
    {
        if (FogOfWar.Instance != null && FogOfWar.Instance.TeamMembers != null)
        {
            for (int i = 0; i < FogOfWar.Instance.TeamMembers.Count; i++)
            {
                var m = FogOfWar.Instance.TeamMembers[i];
                if (m != null && m.character == transform)
                {
                    m.visionRadius = fogVisionRadius;
                    break;
                }
            }
        }

        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0f)
        {
            scanTimer = 0.04f;
            Vector3 myPos = transform.position;
            float radSqr = redSilhouetteDetectionRadius * redSilhouetteDetectionRadius;

            var newlyDetected = new HashSet<FogVisibility>();
            var allEnemies = FindObjectsByType<FogVisibility>(FindObjectsSortMode.None);
            for (int i = 0; i < allEnemies.Length; i++)
            {
                var enemy = allEnemies[i];
                if (enemy == null) continue;

                Vector3 toEnemy = enemy.transform.position - myPos;
                toEnemy.y = 0f;
                if (toEnemy.sqrMagnitude <= radSqr)
                {
                    newlyDetected.Add(enemy);
                    enemy.SetDetectedByLumen(true);
                }
            }

            foreach (var old in detectedEnemies)
            {
                if (old != null && !newlyDetected.Contains(old))
                {
                    old.SetDetectedByLumen(false);
                }
            }

            detectedEnemies.Clear();
            foreach (var n in newlyDetected)
            {
                detectedEnemies.Add(n);
            }
        }
    }

    void OnDisable()
    {
        foreach (var e in detectedEnemies)
        {
            if (e != null) e.SetDetectedByLumen(false);
        }
        detectedEnemies.Clear();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, fogVisionRadius);

        Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, redSilhouetteDetectionRadius);
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
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        mat.renderQueue = 3500;

        var existingLine = transform.Find("LumenPathLine");
        if (existingLine != null) Destroy(existingLine.gameObject);

        var lineObj = new GameObject("LumenPathLine");
        lineObj.transform.SetParent(transform, false);
        pathLine = lineObj.AddComponent<LineRenderer>();
        pathLine.useWorldSpace = true;
        pathLine.positionCount = 2;
        pathLine.startWidth = 0.14f;
        pathLine.endWidth = 0.14f;
        pathLine.material = mat;
        pathLine.sortingOrder = 100;
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
            finalRingLine.startWidth = 0.14f;
            finalRingLine.endWidth = 0.14f;
            finalRingLine.material = mat;
            finalRingLine.sortingOrder = 100;
            finalRingLine.startColor = new Color(1f, 0.85f, 0.2f, 0.95f);
            finalRingLine.endColor = new Color(1f, 0.85f, 0.2f, 0.95f);
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
            Vector3 center = finalPoint.position + Vector3.up * 0.18f;
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
            Color c = new Color(1f, 0.9f, 0.2f, 0.85f);
            pathLine.startColor = c;
            pathLine.endColor = c;
            var pts = WallObstacle.BuildDetourPath(transform.position, finalPoint.position, 0.75f);
            pathLine.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++) pathLine.SetPosition(i, pts[i] + Vector3.up * 0.18f);
        }
        else if (currentMode == LumenMode.FollowAria && ariaTransform != null)
        {
            pathLine.enabled = true;
            Color c = new Color(0.3f, 0.95f, 1f, 0.75f);
            pathLine.startColor = c;
            pathLine.endColor = c;
            var pts = WallObstacle.BuildDetourPath(transform.position, ariaTransform.position, 0.75f);
            pathLine.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++) pathLine.SetPosition(i, pts[i] + Vector3.up * 0.18f);
        }
        else
        {
            pathLine.enabled = false;
        }
    }
}
