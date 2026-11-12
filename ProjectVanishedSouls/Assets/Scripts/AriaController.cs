using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class AriaController : MonoBehaviour
{
    [Header("Taş")]
    public Transform handPoint;        
    public float pickupRange = 3f;     
    public float stoneSpeed = 15f;
    public float throwRange = 10f;

    [Header("AriaThrowRock zamanları (saniye)")]
    public float pickTime   = 0.95f;  // E: 0 → buraya kadar oynar, taş ele geçer (el en altta)
    public float throwStart = 1.75f;  // Sağ tık: animasyon buradan başlar (eğilme atlanır)
    public float releaseAt  = 2.15f;  // taş bu anda elden çıkar
    public float throwEnd   = 2.9f;   // atış biter, Idle'a dön

    [Header("Animator State İsimleri")]
    public string idleState  = "Idle";
    public string walkState  = "AriaWalk";
    public string knifeState = "AriaKnifeAttack";
    public string throwState = "AriaThrowRock";

    public bool HasStone => heldStone != null;

    [Header("Bıçak Görseli")]
    public Renderer[] knifeRenderers; 

    NavMeshAgent agent;
    Animator anim;
    float busyUntil;
    string current;
    GameObject heldStone;
    bool customHand;   
    Vector3 clickGoal;
    bool hasClickGoal;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        anim  = GetComponent<Animator>();
        anim.applyRootMotion = false;
        customHand = handPoint != null;
        if (handPoint == null) handPoint = FindHand(transform);

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
        SetKnifeVisual(false);
        EnemyAI.EnsureSceneConfigured();
    }

    void Start()
    {
        agent.ResetPath();
        PlayState(idleState, 0f);
        SetKnifeVisual(false);
        EnemyAI.EnsureSceneConfigured(); // Başlangıçta elinde hiçbir şey olmasın!
    }

    void FindKnifeObjects()
    {
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            string n = r.gameObject.name.ToLower();

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

            // "knife"
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

        bool moving = agent.hasPath && agent.velocity.magnitude > 0.1f;
        if (moving) anim.speed = 1f; PlayState(moving ? walkState : idleState, 0.15f);
    }

    [Header("Bıçak Ayarları")]
    public float knifeImpactTime = 1.25f;  // Darbenin indiği an (bu anda küp yok olur)
    public float knifeRange = 2.0f;       // Vurabilmek için düşmana yaklaşma mesafesi

    public bool KnifeMode { get; private set; }

    void ToggleKnife()
    {
        KnifeMode = !KnifeMode;
        SetKnifeVisual(KnifeMode);
        Debug.Log(KnifeMode ? "Bıçak çekildi!" : "Bıçak kınına koyuldu.");
    }

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

        StartCoroutine(KnifeStrike(target, target.position - transform.position));
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
                StartCoroutine(KnifeStrike(target, target.position - transform.position));
                yield break;
            }
            yield return null;
        }
    }

    IEnumerator KnifeStrike(Transform target, Vector3 dir)
    {
        hasClickGoal = false;
        agent.ResetPath();
        dir.y = 0;
        if (dir != Vector3.zero) transform.rotation = Quaternion.LookRotation(dir);

        anim.speed = 1f;
        current = knifeState;
        float clipLen = ClipLength(knifeState);
        busyUntil = Time.time + clipLen;

        anim.CrossFadeInFixedTime(knifeState, 0.1f, 0, 0f);

        yield return new WaitForSeconds(knifeImpactTime);

        if (target != null)
        {
            var e = target.GetComponentInParent<Enemy>();
            if (e != null) e.Die(); else Destroy(target.gameObject);
            Debug.Log("Darbe indi, düşman yok oldu!");
        }

        yield return new WaitForSeconds(Mathf.Max(0.1f, clipLen - knifeImpactTime));

        PlayState(idleState, 0.15f);
    }

    bool Busy => Time.time < busyUntil;

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

    void LateUpdate()
    {
        if (heldStone == null) return;
        if (customHand)   
        {
            heldStone.transform.SetPositionAndRotation(handPoint.position, handPoint.rotation);
            return;
        }
        if (handPoint == transform)
        {
            heldStone.transform.position = transform.position + Vector3.up * 1f + transform.forward * 0.4f + transform.right * 0.3f;
            return;
        }
       
        Vector3 palm = handPoint.childCount > 0
            ? Vector3.Lerp(handPoint.position, handPoint.GetChild(0).position, 0.8f)
            : handPoint.position;
        heldStone.transform.position = palm;
    }

    Transform FindNearestStone(out float bestDist)
    {
        Transform best = null; bestDist = float.MaxValue;
        foreach (var t in FindObjectsByType<Transform>())
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

    public void ThrowStoneAt(Transform target)
    {
        if (target == null) return;
        ThrowStoneToPoint(target.position);
    }

    public void ThrowStoneToPoint(Vector3 targetPoint)
    {
        if (Busy || !HasStone) { Debug.Log("Önce E ile taş al!"); return; }
        hasClickGoal = false;

        Vector3 flatDelta = targetPoint - transform.position;
        flatDelta.y = 0f;
        if (flatDelta.magnitude > throwRange)
        {
            targetPoint = transform.position + flatDelta.normalized * throwRange;
        }

        targetPoint = WallObstacle.PushOutOfWalls(targetPoint, 0.45f);
        StartCoroutine(ThrowToPointRoutine(targetPoint));
    }

    IEnumerator ThrowToPointRoutine(Vector3 targetPoint)
    {
        agent.ResetPath();
        Vector3 dir = targetPoint - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir);

        busyUntil = Time.time + (throwEnd - throwStart);
        current = throwState;
        anim.CrossFadeInFixedTime(throwState, 0.15f, 0, throwStart); // sadece atış kısmı

        yield return new WaitForSeconds(Mathf.Max(0f, releaseAt - throwStart));

        if (heldStone == null) yield break;

        GameObject stone = heldStone;
        heldStone = null;
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
            stone.transform.position = Vector3.Lerp(start, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * arcHeight);
            yield return null;
        }
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

        Debug.Log($"Taş {end} noktasına düştü! Yakındaki düşmanlar sesi duyup oraya yöneliyor.");
        EnemyAI.NotifyStoneNoise(end);
    }

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

    void Attack(string state)
    {
        hasClickGoal = false;
        agent.ResetPath();
        PlayState(state, 0.1f, true);
        busyUntil = Time.time + ClipLength(state);
    }

    void PlayState(string state, float fade, bool force = false)
    {
        if (!force && current == state &&
            (anim.IsInTransition(0) || anim.GetCurrentAnimatorStateInfo(0).IsName(state))) return;
        current = state;
        anim.CrossFadeInFixedTime(state, fade, 0, 0f);
    }

    float ClipLength(string state)
    {
        foreach (var c in anim.runtimeAnimatorController.animationClips)
            if (c.name == state) return c.length;
        return 1f;
    }
}
