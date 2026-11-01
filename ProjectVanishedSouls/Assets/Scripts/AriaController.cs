using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class AriaController : MonoBehaviour
{
    [Header("Taş")]
    public Transform handPoint;         // Sol avuçtaki Empty objeyi buraya sürükle (boşsa hand.L kullanılır)
    public float pickupRange = 3f;      // E ile alma mesafesi
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
    public Renderer[] knifeRenderers; // Otomatik bulunur

    NavMeshAgent agent;
    Animator anim;
    float busyUntil;
    string current;
    GameObject heldStone;
    bool customHand;   // handPoint Inspector'dan atandı mı

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        anim  = GetComponent<Animator>();
        anim.applyRootMotion = false;
        customHand = handPoint != null;
        if (handPoint == null) handPoint = FindHand(transform);

        FindKnifeObjects();
        SetKnifeVisual(false);
    }

    void Start()
    {
        agent.ResetPath();
        PlayState(idleState, 0f);
        SetKnifeVisual(false); // Başlangıçta elinde hiçbir şey olmasın!
    }

    void FindKnifeObjects()
    {
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            string n = r.gameObject.name.ToLower();

            // Vücut ve kıyafet materyallerine sahip olanları ASLA gizleme
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

            // Sadece ismi "knife" içerenler veya materyali "Material.006" olanlar bıçaktır
            bool isKnife = n.Contains("knife") || n.Contains("bicak");
            if (!isKnife && r.sharedMaterials != null)
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null && (m.name.Contains("Material.006") || m.name.ToLower().Contains("knife")))
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
                r.gameObject.SetActive(visible);
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
        var kb = Keyboard.current;
        if (kb != null && !Busy)
        {
            if (kb.fKey.wasPressedThisFrame) ToggleKnife();
            if (kb.eKey.wasPressedThisFrame && !HasStone) { SetKnifeVisual(false); StartCoroutine(PickUp()); }
        }

        if (Busy) return;
        bool moving = agent.hasPath && agent.velocity.magnitude > 0.1f;
        if (moving) anim.speed = 1f; PlayState(moving ? walkState : idleState, 0.15f);
    }

    // ---------- F: sadece bıçağı göster / gizle ----------
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

    // ---------- Sağ tık: düşmana bıçakla saldır ----------
    public void KnifeAttackAt(Transform target)
    {
        if (target == null || Busy) return;
        if (!KnifeMode)
        {
            Debug.Log("Bıçak çekili değil! Önce F tuşuna bas.");
            return;
        }

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
        agent.SetDestination(target.position);
        while (target != null && KnifeMode)
        {
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
        agent.ResetPath();
        dir.y = 0;
        if (dir != Vector3.zero) transform.rotation = Quaternion.LookRotation(dir);

        anim.speed = 1f;
        current = knifeState;
        float clipLen = ClipLength(knifeState);
        busyUntil = Time.time + clipLen;

        // Bıçak animasyonunu EN BAŞTAN (0'dan) oynat
        anim.CrossFadeInFixedTime(knifeState, 0.1f, 0, 0f);

        // Darbenin indiği ana kadar bekle
        yield return new WaitForSeconds(knifeImpactTime);

        // Darbe anı: Düşman yok olur!
        if (target != null)
        {
            var e = target.GetComponentInParent<Enemy>();
            if (e != null) e.Die(); else Destroy(target.gameObject);
            Debug.Log("Darbe indi, düşman yok oldu!");
        }

        // Animasyonun kalan kısmını bekle (vuruş tamamlanması)
        yield return new WaitForSeconds(Mathf.Max(0.1f, clipLen - knifeImpactTime));

        // Saldırı bitti: Idle'a dön (Bıçak F ile kapatılana kadar elinde kalır)
        PlayState(idleState, 0.15f);
    }

    bool Busy => Time.time < busyUntil;

    // ---------- E: eğil, yakındaki taşı al ----------
    IEnumerator PickUp()
    {
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

    // Stone bileşeni olan ya da adı Taş/Stone olan en yakın obje (yatay mesafe)
    Transform FindNearestStone(out float bestDist)
    {
        Transform best = null; bestDist = float.MaxValue;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            string n = t.name.ToLower();
            bool isStone = t.GetComponent<Stone>() != null || n.Contains("taş") || n.Contains("tas") || n.Contains("stone") || n.Contains("sphere") || n.Contains("küre");
            if (!isStone || t.IsChildOf(transform)) continue;
            Vector3 d = t.position - transform.position; d.y = 0;
            if (d.magnitude < bestDist) { bestDist = d.magnitude; best = t; }
        }
        return best;
    }

    // ---------- Sağ tık: düşmana fırlat ----------
    public void ThrowStoneAt(Transform target)
    {
        if (Busy || !HasStone) { Debug.Log("Önce E ile taş al!"); return; }
        StartCoroutine(Throw(target));
    }

    IEnumerator Throw(Transform target)
    {
        agent.ResetPath();
        Vector3 dir = target.position - transform.position; dir.y = 0;
        if (dir != Vector3.zero) transform.rotation = Quaternion.LookRotation(dir);

        busyUntil = Time.time + (throwEnd - throwStart);
        current = throwState;
        anim.CrossFadeInFixedTime(throwState, 0.15f, 0, throwStart);  // sadece atış kısmı

        yield return new WaitForSeconds(Mathf.Max(0f, releaseAt - throwStart));

        // Taşı elden bırak ve kavisle hedefin yüzeyine uçur
        GameObject stone = heldStone; heldStone = null;
        Vector3 start = stone.transform.position;
        Collider tc = target.GetComponent<Collider>();
        Vector3 end   = tc != null ? tc.ClosestPoint(start) : target.position;
        end += (start - end).normalized * stone.transform.lossyScale.x * 0.5f;   // içine girmesin
        float flight  = Mathf.Max(0.05f, Vector3.Distance(start, end) / stoneSpeed);
        for (float t = 0; t < 1f; t += Time.deltaTime / flight)
        {
            stone.transform.position = Vector3.Lerp(start, end, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.7f;
            yield return null;
        }
        stone.transform.position = end;

        // Çarptı → taş geri seker ve yere düşer, sonra tekrar alınabilir
        Debug.Log($"Taş {target.name}'a çarptı!");
        Collider sc = stone.GetComponent<Collider>();
        if (sc != null) sc.enabled = true;
        Rigidbody rb = stone.GetComponent<Rigidbody>();
        if (rb == null) rb = stone.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        Vector3 back = start - end; back.y = 0;
        rb.linearVelocity = back.normalized * 2.5f + Vector3.up * 2f;
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

    // ---------- Ortak ----------
    public void Stop() => agent.ResetPath();

    public void MoveTo(Vector3 destination)
    {
        if (!Busy) agent.SetDestination(destination);
    }

    void Attack(string state)
    {
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
