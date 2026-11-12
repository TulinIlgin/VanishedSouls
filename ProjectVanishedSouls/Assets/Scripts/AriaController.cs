using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class AriaController : MonoBehaviour
{
    [SerializeField]private TrailRenderer trail;
    [Header("Taş")]
    public Transform handPoint;         // Sol avuçtaki Empty objeyi buraya sürükle (boşsa hand.L kullanılır)
    public float pickupRange = 3f;      // E ile alma mesafesi
    public float stoneSpeed = 15f;
    public float throwRange = 10f;

    [Header("AriaThrowRock zamanları (saniye)")]
    public float pickTime   = 0.95f;  // E: 0 → buraya kadar oynar, taş ele geçer (el en altta)
    public float throwStart = 1.75f;  // Sağ tık: animasyon buradan başlar (eğilme atlanır)
    public float releaseAt  = 2.15f;  // (Eski zamanlama - artık OnThrowRelease event'i kullanılıyor)
    public float throwEnd   = 2.9f;   // (Eski zamanlama - artık FinishThrow event'i kullanılıyor)

    [Header("Animator State İsimleri")]
    public string idleState  = "Idle";
    public string walkState  = "AriaWalk";
    public string crouchIdleState = "CrouchIdle";
    public string crouchWalkState = "CrouchWalk";
    public string knifeState = "AriaKnifeAttack";
    public string throwState = "AriaThrowRock";

    public bool HasStone => heldStone != null;

    [Header("Bıçak Görseli")]
    public Renderer[] knifeRenderers; // Otomatik bulunur

    NavMeshAgent agent;
    Animator anim;
    float busyUntil;
    string current;
    GameObject heldStone;
    bool customHand;   // handPoint Inspector'dan atandı mı
    Vector3 clickGoal;
    bool hasClickGoal;

    // Yürüme titremesini önlemek için hysteresis zamanlayıcısı
    float walkStopTimer;          // Hız sıfıra düşünce beklenecek süre
    const float WalkStopDelay = 0.12f; // saniye — bu kadar durup "gerçekten durdu" denir
    bool isWalkingSmooth;         // Titremeye karşı yumuşatılmış yürüme durumu

    Transform pendingKnifeTarget;
    Vector3 pendingThrowPoint;
    bool hasPendingThrow;
    bool isPerformingAction;
    float actionStartTime;

    public bool Busy => isPerformingAction || Time.time < busyUntil;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        anim  = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.applyRootMotion = false;

        // Eğer Animator bir alt objedeyse köprü script'i ekle
        if (anim != null && anim.gameObject != gameObject)
        {
            var relay = anim.GetComponent<AriaAnimationEvents>();
            if (relay == null) anim.gameObject.AddComponent<AriaAnimationEvents>();
        }

        customHand = handPoint != null;
        if (handPoint == null) handPoint = FindHand(transform);

        // Sahnede Inspector'dan hatalı kaydedilmiş animasyon/menzil değerlerini düzelt
        if (releaseAt >= throwEnd || releaseAt > 2.3f) releaseAt = 2.15f;
        if (throwEnd < 2.5f) throwEnd = 2.9f;
        if (throwRange < 18f) throwRange = 18f;
        if (pickupRange < 4.5f) pickupRange = 4.5f;

        FindKnifeObjects();

        if (GetComponent<PlayerCharacter>() == null)
        {
            var pc = gameObject.AddComponent<PlayerCharacter>();
            pc.characterName = "Aria";
        }

        if (GetComponent<PlayerStealth>() == null)
        {
            gameObject.AddComponent<PlayerStealth>();
        }
        SetKnifeVisual(false);
        EnemyAI.EnsureSceneConfigured();
    }

    void Start()
    {
        agent.ResetPath();
        PlayState(idleState, 0f);
        SetKnifeVisual(false);
        EnemyAI.EnsureSceneConfigured();
    }

    void FindKnifeObjects()
    {
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            string n = r.gameObject.name.ToLower();

            // Vücut ve kıyafet materyallerine sahip olanları gizleme
            bool isBody = false;
            if (r.sharedMaterials != null)
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null)
                    {
                        string mn = m.name.ToLower();
                        if (mn.Contains("hand") || mn.Contains("arm") || mn.Contains("shoe") ||
                            mn.Contains("cloth") || mn.Contains("head") || mn.Contains("hair") ||
                            mn.Contains("leg") || mn.Contains("sock") || mn.Contains("eye"))
                        {
                            isBody = true;
                            break;
                        }
                    }
                }
            }
            if (isBody) continue;

            bool isKnife = n.Contains("knife") || n.Contains("bicak");
            if (!isKnife && r.sharedMaterials != null)
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null && m.name.ToLower().Contains("knife"))
                    {
                        isKnife = true;
                        break;
                    }
                }
            }

            if (isKnife)
            {
                list.Add(r);
                Debug.Log($"Bıçak parçası: {r.gameObject.name} (Material: {(r.sharedMaterial != null ? r.sharedMaterial.name : "none")})");
            }
        }
        knifeRenderers = list.ToArray();
    }

    public void SetKnifeVisual(bool visible)
    {
        if (knifeRenderers == null) return;
        foreach (var r in knifeRenderers)
        {
            if (r != null)
            {
                r.enabled = visible;
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, throwRange);
    }

    void Update()
    {
        if (GameMenuManager.IsMenuOpen) return;

        // Aksiyon güvenlik kontrolü (animasyon hızlandırıldığında event kaçırılırsa veya takılı kalırsa):
        if (isPerformingAction)
        {
            if (hasPendingThrow)
            {
                bool leftThrowState = anim != null && !anim.GetCurrentAnimatorStateInfo(0).IsName(throwState) && !anim.IsInTransition(0);
                bool timeout = Time.time >= actionStartTime + 2.0f;
                if (leftThrowState || timeout)
                {
                    OnThrowRelease();
                    FinishThrow();
                }
            }
            else if (pendingKnifeTarget != null)
            {
                bool leftKnifeState = anim != null && !anim.GetCurrentAnimatorStateInfo(0).IsName(knifeState) && !anim.IsInTransition(0);
                bool timeout = Time.time >= actionStartTime + 2.0f;
                if (leftKnifeState || timeout)
                {
                    OnKnifeHit();
                    FinishKnifeAttack();
                }
            }
        }

        var kb = Keyboard.current;
        if (kb != null && !Busy)
        {
            if (kb.fKey.wasPressedThisFrame) ToggleKnife();
            if (kb.eKey.wasPressedThisFrame && !HasStone)
            {
                KnifeMode = false;
                SetKnifeVisual(false);
                StartCoroutine(PickUp());
            }
        }

        if (Busy) return;

        // Tıklanan hedef ile Aria arasında duvar varsa etrafından dolanacak köşeye yönlendir
        if (hasClickGoal)
        {
            Vector3 toGoal = clickGoal - transform.position;
            toGoal.y = 0f;
            if (toGoal.magnitude <= 0.35f)
            {
                hasClickGoal = false;
            }
            else
            {
                Vector3 steered = WallObstacle.GetSteeredTarget(transform.position, clickGoal, 0.75f);
                if (Vector3.Distance(agent.destination, steered) > 0.2f)
                    agent.SetDestination(steered);
            }
        }

        transform.position = WallObstacle.PushOutOfWalls(transform.position, 0.5f);

        // Eğilme (Crouch) kontrolü: Karakter çalıdaysa kodun zorla Idle oynatıp animasyonu bölmesini engelle!
        bool isCrouching = false;
        var stealth = GetComponent<PlayerStealth>();
        if (stealth != null && stealth.isHidden)
        {
            isCrouching = true;
        }
        else if (anim != null)
        {
            foreach (var p in anim.parameters)
            {
                if (p.name == "isCrouching")
                {
                    isCrouching = anim.GetBool("isCrouching");
                    break;
                }
            }
        }

        // Hysteresis: hız eşiği geçilince hemen "yürüyor" say,
        // ama hız düşünce WalkStopDelay kadar bekle — titreme önlenir.
        bool rawMoving = agent.hasPath && agent.velocity.magnitude > 0.15f;
        if (rawMoving)
        {
            isWalkingSmooth = true;
            walkStopTimer = WalkStopDelay;
        }
        else
        {
            walkStopTimer -= Time.deltaTime;
            if (walkStopTimer <= 0f) isWalkingSmooth = false;
        }
        bool moving = isWalkingSmooth;
        if (moving) anim.speed = 1f;

        // Animator parametrelerini güncelle (Animator'daki geçiş oklarının düzgün çalışması için şart!)
        if (anim != null)
        {
            SetAnimBoolIfExists("IsWalking", moving);
            SetAnimBoolIfExists("isWalking", moving);
            SetAnimBoolIfExists("isCrouching", isCrouching);
        }

        if (isCrouching)
        {
            string targetState = moving
                ? ((HasState(crouchWalkState) || HasClip(crouchWalkState)) ? crouchWalkState : walkState)
                : ((HasState(crouchIdleState) || HasClip(crouchIdleState)) ? crouchIdleState : idleState);
            PlayState(targetState, 0.15f);
        }
        else
        {
            PlayState(moving ? walkState : idleState, 0.15f);
        }
    }

    // ---------- F: sadece bıçağı göster / gizle ----------
    [Header("Bıçak Ayarları")]
    public float knifeRange = 2.0f;       // Vurabilmek için düşmana yaklaşma mesafesi

    public bool KnifeMode { get; private set; }

    void ToggleKnife()
    {
        KnifeMode = !KnifeMode;
        SetKnifeVisual(KnifeMode);
        Debug.Log(KnifeMode ? "Bıçak çekildi!" : "Bıçak kınına koyuldu.");
    }

    // ---------- Sağ tık: düşmana bıçakla saldır ----------
    public void KnifeAttackAt(Transform target)
    {
        if (target == null || Busy) return;
        if (!KnifeMode)
        {
            Debug.Log("Bıçak çekili değil! Önce F tuşuna bas.");
            return;
        }

        hasClickGoal = false;
        Collider col = target.GetComponent<Collider>();
        Vector3 closest = col != null ? col.ClosestPoint(transform.position) : target.position;
        Vector3 d = closest - transform.position; d.y = 0;

        if (d.magnitude > knifeRange)
        {
            StopCoroutine("MoveAndStrike");
            StartCoroutine(MoveAndStrike(target));
            return;
        }

        StartKnifeAttack(target, target.position - transform.position);
    }

    IEnumerator MoveAndStrike(Transform target)
    {
        while (target != null && KnifeMode)
        {
            Vector3 steered = WallObstacle.GetSteeredTarget(transform.position, target.position, 0.75f);
            agent.SetDestination(steered);

            Collider col = target.GetComponent<Collider>();
            Vector3 closest = col != null ? col.ClosestPoint(transform.position) : target.position;
            Vector3 d = closest - transform.position; d.y = 0;
            if (d.magnitude <= knifeRange)
            {
                agent.ResetPath();
                StartKnifeAttack(target, target.position - transform.position);
                yield break;
            }
            yield return null;
        }
    }

    void StartKnifeAttack(Transform target, Vector3 dir)
    {
        hasClickGoal = false;
        agent.ResetPath();
        dir.y = 0;
        if (dir != Vector3.zero) transform.rotation = Quaternion.LookRotation(dir);

        pendingKnifeTarget = target;
        isPerformingAction = true;
        actionStartTime = Time.time;
        anim.speed = 1f;
        current = knifeState;

        // Bıçak animasyonunu EN BAŞTAN (0'dan) oynat
        anim.CrossFadeInFixedTime(knifeState, 0.1f, 0, 0f);
        Debug.Log("Aria: Bıçak saldırısına başladı (OnKnifeHit darbeyi indirir, FinishKnifeAttack hareketi açar).");
    }

    // =========================================================================
    // BIÇAK SALDIRISI ANİMASYON EVENT'LERİ
    // =========================================================================

    /// <summary>
    /// Bıçağın düşmana saplandığı / darbenin indiği kareye ekleyin.
    /// </summary>
    public void OnKnifeHit()
    {
        if (pendingKnifeTarget != null)
        {
            var e = pendingKnifeTarget.GetComponentInParent<Enemy>();
            if (e != null) e.Die(); else Destroy(pendingKnifeTarget.gameObject);
            Debug.Log("Aria: Bıçak darbesi indi, düşman öldü (OnKnifeHit Event)!");
            pendingKnifeTarget = null;
        }
    }

    /// <summary>
    /// Bıçak animasyonunun EN SON karesine ekleyin. Bu çağrılana kadar Aria hareket edemez; çağrılınca Idle'a döner.
    /// </summary>
    public void FinishKnifeAttack()
    {
        if (pendingKnifeTarget != null)
        {
            OnKnifeHit();
        }
        isPerformingAction = false;
        pendingKnifeTarget = null;
        busyUntil = 0f;
        PlayState(idleState, 0.15f);
        Debug.Log("Aria: Bıçak saldırısı tamamlandı, hareket serbest (FinishKnifeAttack Event).");
    }

    // ---------- E: eğil, yakındaki taşı al ----------
    IEnumerator PickUp()
    {
        hasClickGoal = false;
        Transform target = FindNearestStone(out float dist);

        agent.ResetPath();
        if (target != null && dist <= pickupRange)
        {
            Vector3 dir = target.position - transform.position; dir.y = 0;
            if (dir != Vector3.zero) transform.rotation = Quaternion.LookRotation(dir);
        }

        // Animasyon her durumda oynar
        busyUntil = Time.time + pickTime;
        PlayState(throwState, 0.1f, true);
        yield return new WaitForSeconds(pickTime);   // ele değdiği an
        busyUntil = 0f;                              // atış kısmını oynatma, Idle'a dön

        if (target == null) { Debug.Log("Sahnede taş yok! Küreye 'Stone' bileşeni ekle ya da adını 'Taş' yap."); yield break; }
        if (dist > pickupRange) { Debug.Log($"Taş çok uzak: {dist:F1} m (menzil {pickupRange} m)"); yield break; }

        heldStone = target.gameObject;
        var c = heldStone.GetComponent<Collider>(); if (c != null) c.enabled = false;
        var rb = heldStone.GetComponent<Rigidbody>(); if (rb != null) rb.isKinematic = true;
        heldStone.transform.SetParent(null);
    }

    // Taş elde: her karede tutma noktasına taşı (ölçek bozulmaz)
    void LateUpdate()
    {
        if (heldStone == null) return;
        if (customHand)   // Inspector'dan atanan Empty obje → tam orada dur
        {
            heldStone.transform.SetPositionAndRotation(handPoint.position, handPoint.rotation);
            return;
        }
        if (handPoint == transform)
        {
            heldStone.transform.position = transform.position + Vector3.up * 1f + transform.forward * 0.4f + transform.right * 0.3f;
            return;
        }
        // Kemik: bilekten parmaklara doğru → avuç içi
        Vector3 palm = handPoint.childCount > 0
            ? Vector3.Lerp(handPoint.position, handPoint.GetChild(0).position, 0.8f)
            : handPoint.position;
        heldStone.transform.position = palm;
    }

    // Stone bileşeni olan ya da adı Sphere/Stone olan en yakın taşınabilir obje
    Transform FindNearestStone(out float bestDist)
    {
        Transform best = null; bestDist = float.MaxValue;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null || t.IsChildOf(transform)) continue;
            string n = t.name.ToLower();
            if (n.Contains("nokta") || n.Contains("final") || n.Contains("lumen")) continue;

            bool isStone = t.GetComponent<Stone>() != null || n == "sphere" || n.StartsWith("sphere") ||
                           n == "taş" || n == "tas" || n.StartsWith("stone") || n.Contains("küre");
            if (!isStone) continue;

            Vector3 d = t.position - transform.position; d.y = 0;
            if (d.magnitude < bestDist) { bestDist = d.magnitude; best = t; }
        }
        return best;
    }

    // ---------- Sağ tık: sağ tıklanan noktaya dikkat dağıtmak için taş fırlat ----------
    public void ThrowStoneAt(Transform target)
    {
        if (target == null) return;
        ThrowStoneToPoint(target.position);
    }

    public void ThrowStoneToPoint(Vector3 targetPoint)
    {
        if (Busy || !HasStone) { Debug.Log("Önce E ile taş al!"); return; }
        hasClickGoal = false;

        // Menzil sınırını aşarsa menzil içindeki en uç noktaya at
        Vector3 flatDelta = targetPoint - transform.position;
        flatDelta.y = 0f;
        if (flatDelta.magnitude > throwRange)
        {
            targetPoint = transform.position + flatDelta.normalized * throwRange;
        }

        targetPoint = WallObstacle.PushOutOfWalls(targetPoint, 0.45f);

        agent.ResetPath();
        Vector3 dir = targetPoint - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir);

        pendingThrowPoint = targetPoint;
        hasPendingThrow = true;
        isPerformingAction = true;
        actionStartTime = Time.time;
        current = throwState;

        float clipLen = ClipLength(throwState);
        float normalizedStart = 0f;
        if (clipLen > 2.2f && throwStart > 0f)
        {
            normalizedStart = Mathf.Clamp01(throwStart / clipLen);
        }

        anim.CrossFade(throwState, 0.05f, 0, normalizedStart);
        Debug.Log("Aria: Taş fırlatma başladı (OnThrowRelease taşı atar, FinishThrow bitirir).");
    }

    // =========================================================================
    // TAŞ FIRLATMA ANİMASYON EVENT'LERİ
    // =========================================================================

    /// <summary>
    /// Taşın elden fırlatıldığı karede çağrılır: Taş kavisle hedefe uçar.
    /// </summary>
    public void OnThrowRelease()
    {
        if (heldStone == null) return;

        hasPendingThrow = false;
        GameObject stone = heldStone;
        heldStone = null;

        StartCoroutine(FlyStoneRoutine(stone, pendingThrowPoint));
        Debug.Log("Aria: Taş elden fırlatıldı (OnThrowRelease Event)!");
    }

    IEnumerator FlyStoneRoutine(GameObject stone, Vector3 targetPoint)
    {
        Vector3 start = stone.transform.position;

        float stoneRadius = Mathf.Max(0.12f, stone.transform.lossyScale.y * 0.5f);
        Vector3 end = new Vector3(targetPoint.x, transform.position.y + stoneRadius, targetPoint.z);
        if (Physics.Raycast(targetPoint + Vector3.up * 3f, Vector3.down, out RaycastHit groundHit, 8f, ~((1 << 2) | (1 << 6) | (1 << 8))))
        {
            end.y = groundHit.point.y + stoneRadius;
        }

        float dist = Vector3.Distance(start, end);
        float flight = Mathf.Max(0.12f, dist / Mathf.Max(1f, stoneSpeed));
        float arcHeight = Mathf.Clamp(dist * 0.22f, 0.5f, 1.6f);

        for (float t = 0f; t < 1f; t += Time.deltaTime / flight)
        {
            if (stone == null) yield break;
            stone.transform.position = Vector3.Lerp(start, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * arcHeight);
            yield return null;
        }

        if (stone != null)
        {
            stone.transform.position = end;

            Collider sc = stone.GetComponent<Collider>();
            if (sc != null) sc.enabled = true;
            Rigidbody rb = stone.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            Debug.Log($"Taş {end} noktasına düştü! Yakındaki düşmanlar sesi duyuyor.");
            EnemyAI.NotifyStoneNoise(end);
        }
    }

    /// <summary>
    /// Taş atış animasyonunun EN SON karesinde çağrılır: Hareketi açar ve Idle'a döner.
    /// </summary>
    public void FinishThrow()
    {
        if (heldStone != null)
        {
            OnThrowRelease();
        }
        isPerformingAction = false;
        hasPendingThrow = false;
        busyUntil = 0f;
        PlayState(idleState, 0.15f);
        Debug.Log("Aria: Taş atışı tamamlandı, hareket serbest (FinishThrow Event).");
    }

    /// <summary>
    /// Ortak bitiş fonksiyonu (Her iki animasyon için de FinishAttack seçilirse çalışır)
    /// </summary>
    public void FinishAttack()
    {
        if (hasPendingThrow) FinishThrow();
        else FinishKnifeAttack();
    }

    public void OnAttackHit() => OnKnifeHit();
    public void ReleaseStone() => OnThrowRelease();
    public void ThrowStone() => OnThrowRelease();

    Transform FindHand(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>())
        {
            string n = t.name.ToLower();
            if (n.Contains("hand") && (n.EndsWith(".l") || n.EndsWith("_l") || n.Contains("left")))
                return t;
        }
        Debug.LogWarning("Sol el kemiği bulunamadı, Hand Point alanına elle ata.");
        return transform;
    }

    // ---------- Ortak ----------
    public void Stop()
    {
        hasClickGoal = false;
        agent.ResetPath();
    }

    public void MoveTo(Vector3 destination)
    {
        if (Busy) return;
        if (NavMesh.SamplePosition(destination, out NavMeshHit navHit, 3.0f, NavMesh.AllAreas))
            clickGoal = navHit.position;
        else
            clickGoal = destination;

        clickGoal = WallObstacle.PushOutOfWalls(clickGoal, 0.65f);
        hasClickGoal = true;

        Vector3 steered = WallObstacle.GetSteeredTarget(transform.position, clickGoal, 0.75f);
        agent.SetDestination(steered);
    }

    void PlayState(string state, float fade, bool force = false)
    {
        if (!force && current == state &&
            (anim.IsInTransition(0) || anim.GetCurrentAnimatorStateInfo(0).IsName(state))) return;
        current = state;
        anim.CrossFadeInFixedTime(state, fade, 0, 0f);
    }

    void SetAnimBoolIfExists(string paramName, bool val)
    {
        if (anim == null) return;
        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool &&
                string.Equals(p.name, paramName, System.StringComparison.OrdinalIgnoreCase))
            {
                if (anim.GetBool(p.name) != val)
                    anim.SetBool(p.name, val);
                return;
            }
        }
    }

    bool HasState(string stateName)
    {
        if (anim == null || string.IsNullOrEmpty(stateName)) return false;
        return anim.HasState(0, Animator.StringToHash(stateName));
    }

    bool HasClip(string stateName)
    {
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            foreach (var c in anim.runtimeAnimatorController.animationClips)
                if (c.name == stateName) return true;
        }
        return false;
    }

    float ClipLength(string state)
    {
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            foreach (var c in anim.runtimeAnimatorController.animationClips)
                if (c.name == state) return c.length;
        }
        return 1f;
    }

    public void StartTrail()
    {
        trail.emitting = true;
    }

    // İzi Durdurur (Yeni iz çizmez, mevcut iz ekranda sönerek kaybolur)
    public void StopTrail()
    {
        trail.emitting = false;
    }
}
