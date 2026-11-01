using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyAI : Enemy
{
    public enum State { Patrol, Chase, Search, Attack }

    [Header("Devriye Ayarları (Noktalar)")]
    [Tooltip("Düşmanın sırayla gezeceği devriye noktaları (Patrol1, Patrol2 vb.)")]
    public Transform[] patrolPoints;
    public float patrolSpeed = 2.0f;
    public float waitTimeAtPoint = 1.2f;

    [Header("Üçgen Görüş Alanı (FOV)")]
    [Range(20f, 160f)] public float viewAngle = 70f;
    public float viewDistance = 8.0f;
    public float eyeHeight = 1.3f;
    public bool drawConeOnGround = true;

    [Header("Saldırı ve Kovalama (Fiziksel Temas Mesafesi)")]
    public float chaseSpeed = 2.5f;
    [Tooltip("Düşmanın vurabilmesi için karaktere fiziksel olarak dokunma mesafesi (metre)")]
    public float attackRange = 0.55f;
    public float attackCooldown = 1.1f;

    [Header("Bağlı Görsel Model (Küp-Düşman Bağlantısı)")]
    public Transform visualModel;
    public Transform[] extraVisualParts;

    [Header("Anlık Durum")]
    public State currentState = State.Patrol;

    struct BoundPart
    {
        public Transform part;
        public Vector3 localPosOffset;
        public Quaternion localRotOffset;
    }

    Animator anim;
    PlayerCharacter targetPlayer;
    Vector3 lastSeenPosition;
    int currentPointIndex;
    float stateTimer;
    float nextAttackTime;
    float baseGroundY;
    float currentLungeOffset;
    bool isLunging;

    LineRenderer coneLine;
    MeshFilter coneMeshFilter;
    MeshRenderer coneMeshRenderer;
    Mesh coneMesh;
    readonly List<BoundPart> boundParts = new List<BoundPart>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoConfigureSceneOnPlay()
    {
        EnsureSceneConfigured();
    }

    public static void EnsureSceneConfigured()
    {
        // 1. Eski test küpünü ("Cube") kapat
        foreach (var go in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            if (go != null && go.name == "Cube" && go.transform.parent == null && go.GetComponent<BoxCollider>() != null)
            {
                go.SetActive(false);
            }
        }

        // 2. Çimen kök objesine ("Sketchfab_model.003" / "g2_0") ve altındaki tüm çimenlere (Object_4.001 ve Object_5*) Bush kur,
        //    "Cube.007" objesini de duvar (WallObstacle) olarak ayarla
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null) continue;
            if (t.name == "Sketchfab_model.003" || t.name == "g2_0" ||
                ((t.name == "Object_4.001" || t.name.StartsWith("Object_5")) && t.GetComponent<Renderer>() != null))
            {
                var bush = t.GetComponent<Bush>();
                if (bush == null) bush = t.gameObject.AddComponent<Bush>();
                bush.SetupGrassColliders();
            }
            else if (t.name == "Cube.007")
            {
                var wall = t.GetComponent<WallObstacle>();
                if (wall == null) wall = t.gameObject.AddComponent<WallObstacle>();
                wall.SetupWall();
            }
        }

        // 3. Aria'da PlayerCharacter olduğundan emin ol
        var aria = FindFirstObjectByType<AriaController>();
        if (aria != null && aria.GetComponent<PlayerCharacter>() == null)
        {
            var pc = aria.gameObject.AddComponent<PlayerCharacter>();
            pc.characterName = "Aria";
        }

        // 4. Sarı karakteri (Sketchfab_model -> Object_2) Lumen olarak kur ve LumenFinalNoktasi oluştur
        Transform lumenVisual = null;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null) continue;
            if (t.name == "Sketchfab_model")
            {
                t.gameObject.SetActive(true);
                // Eski doğrudan eklenmiş PlayerCharacter varsa hemen temizle (sadece Lumen kök objesi kullanılacak)
                var oldPc = t.GetComponent<PlayerCharacter>();
                if (oldPc != null && t.GetComponent<LumenController>() == null)
                {
                    oldPc.enabled = false;
                    PlayerCharacter.AllPlayers.Remove(oldPc);
                    if (Application.isPlaying) Destroy(oldPc);
                    else DestroyImmediate(oldPc);
                }

                var r = t.GetComponentInChildren<Renderer>(true);
                if (r != null)
                {
                    r.gameObject.SetActive(true);
                    lumenVisual = r.transform;
                }
            }
        }

        if (lumenVisual != null)
        {
            SetupOrUpdateLumenHost(lumenVisual);
        }

        // 5. FBX içindeki "Enemy" düğümünde hatalı NavMeshAgent veya EnemyAI varsa devre dışı bırak
        foreach (var ai in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
        {
            if (ai != null && ai.visualModel == null && ai.transform.parent != null && ai.transform.name == "Enemy")
            {
                var badAgent = ai.GetComponent<NavMeshAgent>();
                if (badAgent != null) badAgent.enabled = false;
                ai.enabled = false;
            }
        }

        // 6. İki kırmızı düşman modeli için Dusman_1 ve Dusman_2 kök objelerini hazırla
        Transform mesh1 = null, mesh2 = null, leftEye = null, rightEye = null;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null) continue;
            if (t.name == "Enemy_Humainoid_Enemy_0") mesh1 = t;
            else if (t.name == "Enemy_Humainoid_Enemy_0.001") mesh2 = t;
            else if (t.name == "Left_Eye") leftEye = t;
            else if (t.name == "Right_Eye") rightEye = t;
        }

        if (mesh1 != null)
            SetupOrUpdateEnemyHost("Dusman_1", mesh1, new Transform[] { leftEye, rightEye }, "Patrol1", "Patrol2");

        if (mesh2 != null)
            SetupOrUpdateEnemyHost("Dusman_2", mesh2, null, "Patrol3", "Patrol4");
    }

    static void SetupOrUpdateLumenHost(Transform lumenVisualMesh)
    {
        var r = lumenVisualMesh.GetComponent<Renderer>();
        if (r == null) return;

        foreach (var col in lumenVisualMesh.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        Bounds b = r.bounds;
        Vector3 footPos = new Vector3(b.center.x, b.min.y, b.center.z);

        GameObject lumenHost = GameObject.Find("Lumen");
        if (lumenHost == null)
        {
            lumenHost = new GameObject("Lumen");
            lumenHost.transform.position = footPos;
            lumenHost.transform.rotation = Quaternion.identity;
        }
        lumenHost.SetActive(true);

        var pc = lumenHost.GetComponent<PlayerCharacter>();
        if (pc == null) pc = lumenHost.AddComponent<PlayerCharacter>();
        pc.characterName = "Lumen";
        pc.maxHealth = 1;
        pc.diePermanentlyOnHit = true;

        var lumenCtrl = lumenHost.GetComponent<LumenController>();
        if (lumenCtrl == null) lumenCtrl = lumenHost.AddComponent<LumenController>();
        lumenCtrl.visualModel = lumenVisualMesh;
        lumenCtrl.leadSpeed = 1.15f;
        lumenCtrl.followSpeed = 1.55f;

        GameObject finalObj = GameObject.Find("LumenFinalNoktasi");
        if (finalObj == null)
        {
            finalObj = new GameObject("LumenFinalNoktasi");
            // Haritanın sol üst ucunda, düşmanların ve çimenlerin ilerisinde varsayılan Final Noktası
            finalObj.transform.position = new Vector3(-23.5f, footPos.y, 17.5f);
        }
        lumenCtrl.finalPoint = finalObj.transform;

        if (Application.isPlaying)
            lumenCtrl.BindVisualModel();
    }

    static void SetupOrUpdateEnemyHost(string hostName, Transform visualMesh, Transform[] extraParts, string p1Name, string p2Name)
    {
        var r = visualMesh.GetComponentInChildren<Renderer>();
        if (r == null) return;

        foreach (var col in visualMesh.GetComponentsInChildren<Collider>(true))
            col.enabled = false;
        if (extraParts != null)
        {
            foreach (var ep in extraParts)
            {
                if (ep == null) continue;
                foreach (var col in ep.GetComponentsInChildren<Collider>(true))
                    col.enabled = false;
            }
        }

        Bounds b = r.bounds;
        Vector3 footPos = new Vector3(b.center.x, b.min.y, b.center.z);

        GameObject host = GameObject.Find(hostName);
        if (host == null)
        {
            host = new GameObject(hostName);
            host.transform.position = footPos;

            Vector3 facing = -Vector3.ProjectOnPlane(visualMesh.up, Vector3.up);
            if (facing.sqrMagnitude < 0.01f) facing = Vector3.ProjectOnPlane(visualMesh.forward, Vector3.up);
            if (facing.sqrMagnitude > 0.01f)
                host.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
        }

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        host.layer = enemyLayer >= 0 ? enemyLayer : 8;

        var cap = host.GetComponent<CapsuleCollider>();
        if (cap == null) cap = host.AddComponent<CapsuleCollider>();
        cap.center = new Vector3(0f, 1.0f, 0f);
        cap.height = 2.2f;
        cap.radius = 0.65f;

        var nav = host.GetComponent<NavMeshAgent>();
        if (nav != null) nav.enabled = false;

        var ai = host.GetComponent<EnemyAI>();
        if (ai == null) ai = host.AddComponent<EnemyAI>();
        ai.visualModel = visualMesh;
        ai.extraVisualParts = extraParts;
        // Sadece düşman fiziksel olarak dokunduğunda (0.55m) vursun!
        ai.attackRange = 0.55f;
        ai.chaseSpeed = 2.5f;

        if (ai.patrolPoints == null || ai.patrolPoints.Length == 0 || ai.patrolPoints[0] == null)
        {
            GameObject p1 = GameObject.Find(p1Name);
            if (p1 == null) p1 = new GameObject(p1Name);

            GameObject p2 = GameObject.Find(p2Name);
            if (p2 == null) p2 = new GameObject(p2Name);

            if (Vector3.Distance(p1.transform.position, p2.transform.position) < 0.25f)
            {
                Vector3 right = host.transform.right;
                p1.transform.position = footPos + right * 2.8f;
                p2.transform.position = footPos - right * 2.8f;
            }

            ai.patrolPoints = new Transform[] { p1.transform, p2.transform };
        }

        if (Application.isPlaying)
            ai.BindVisualParts();
    }

    void Awake()
    {
        if (visualModel == null && transform.parent != null && gameObject.name == "Enemy")
        {
            var badAgent = GetComponent<NavMeshAgent>();
            if (badAgent != null) badAgent.enabled = false;
            EnsureSceneConfigured();
            enabled = false;
            return;
        }

        var nav = GetComponent<NavMeshAgent>();
        if (nav != null) nav.enabled = false;

        // Sahnede eski uzak attackRange (1.6 / 1.8) kayıtlıysa fiziksel temas mesafesine (0.55m) indir
        if (attackRange > 0.65f) attackRange = 0.55f;
        if (chaseSpeed > 2.8f) chaseSpeed = 2.5f;

        anim = GetComponentInChildren<Animator>();
        baseGroundY = transform.position.y;

        BindVisualParts();

        if (drawConeOnGround)
            SetupVisionConeRenderer();
    }

    public void BindVisualParts()
    {
        if (boundParts.Count > 0 || visualModel == null) return;
        boundParts.Clear();

        AddBoundPart(visualModel);
        if (extraVisualParts != null)
        {
            foreach (var p in extraVisualParts)
                AddBoundPart(p);
        }
    }

    void AddBoundPart(Transform part)
    {
        if (part == null) return;
        foreach (var col in part.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        Quaternion invRot = Quaternion.Inverse(transform.rotation);
        boundParts.Add(new BoundPart
        {
            part = part,
            localPosOffset = invRot * (part.position - transform.position),
            localRotOffset = invRot * part.rotation
        });
    }

    void Start()
    {
        EnsureSceneConfigured();
        baseGroundY = transform.position.y;
    }

    void Update()
    {
        if (dead) return;

        LookForPlayers();
        UpdateVisionConeVisual();

        switch (currentState)
        {
            case State.Patrol:
                HandlePatrol();
                break;
            case State.Chase:
                HandleChase();
                break;
            case State.Search:
                HandleSearch();
                break;
            case State.Attack:
                HandleAttack();
                break;
        }
    }

    void LateUpdate()
    {
        if (dead || boundParts.Count == 0) return;

        Vector3 lungeWorld = GetForward() * currentLungeOffset;
        for (int i = 0; i < boundParts.Count; i++)
        {
            var bp = boundParts[i];
            if (bp.part == null) continue;
            bp.part.position = (transform.position + lungeWorld) + transform.rotation * bp.localPosOffset;
            bp.part.rotation = transform.rotation * bp.localRotOffset;
        }
    }

    public override void Die()
    {
        if (dead) return;
        for (int i = 0; i < boundParts.Count; i++)
        {
            if (boundParts[i].part != null)
                Destroy(boundParts[i].part.gameObject);
        }
        base.Die();
    }

    Vector3 GetForward()
    {
        Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        return f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
    }

    // ---------- HEDEF NOKTAYA DOĞRU AKICI YÜRÜME VE DÖNME (DUVARLARDAN DOLANARAK) ----------
    bool MoveTowardsPoint(Vector3 targetWorldPos, float speed, float stopDist)
    {
        Vector3 myPos = transform.position;
        Vector3 toGoal = targetWorldPos - myPos;
        toGoal.y = 0f;
        float distToGoal = toGoal.magnitude;

        if (distToGoal <= stopDist)
            return true;

        // Önünde duvar (Cube.007 vb.) varsa etrafından dolanacak köşe noktasını al
        Vector3 steeredTarget = WallObstacle.GetSteeredTarget(myPos, targetWorldPos, 0.75f);
        Vector3 toSteered = steeredTarget - myPos;
        toSteered.y = 0f;
        Vector3 moveDir = toSteered.sqrMagnitude > 0.0001f ? toSteered.normalized : (toGoal / distToGoal);

        if (moveDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
        }

        Vector3 nextPos = myPos + moveDir * (speed * Time.deltaTime);
        nextPos.y = baseGroundY;
        nextPos = WallObstacle.PushOutOfWalls(nextPos, 0.5f);
        transform.position = nextPos;
        return false;
    }

    // ---------- ÜÇGEN GÖRÜŞ ALANI KONTROLÜ (Sadece FARK EDER ve KOVALAMAYA BAŞLAR, uzaktan öldürmez!) ----------
    void LookForPlayers()
    {
        if (PlayerCharacter.AllPlayers.Count == 0)
        {
            foreach (var pc in FindObjectsByType<PlayerCharacter>(FindObjectsSortMode.None))
            {
                if (pc != null && !pc.IsDead && !PlayerCharacter.AllPlayers.Contains(pc))
                    PlayerCharacter.AllPlayers.Add(pc);
            }
        }

        Vector3 myPos = transform.position;
        Vector3 fwd = GetForward();

        PlayerCharacter bestTarget = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < PlayerCharacter.AllPlayers.Count; i++)
        {
            var player = PlayerCharacter.AllPlayers[i];
            if (player == null || !player.isActiveAndEnabled || player.IsDead) continue;

            Vector3 playerPos = player.WorldPosition;

            // Çimende (veya çimenlerin arasında / Aria ile birlikte çimende) gizleniyorsa ASLA görme!
            if (player.isHidden || Bush.IsPointInAnyBush(playerPos)) continue;

            // Arada duvar (Cube.007 vb.) varsa duvarın arkasını görme!
            if (WallObstacle.IsSegmentBlocked(myPos, playerPos, 0.05f)) continue;

            Vector3 toPlayer = playerPos - myPos;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;

            if (dist > viewDistance) continue;

            float angle = dist > 0.05f ? Vector3.Angle(fwd, toPlayer.normalized) : 0f;
            bool insideTriangle = angle <= (viewAngle * 0.5f + 2f);
            bool touchingBody = dist <= 0.6f;

            if (!insideTriangle && !touchingBody) continue;

            if (dist < bestDist)
            {
                bestDist = dist;
                bestTarget = player;
            }
        }

        if (bestTarget != null)
        {
            targetPlayer = bestTarget;
            lastSeenPosition = bestTarget.WorldPosition;
            if (currentState != State.Attack && currentState != State.Chase)
            {
                currentState = State.Chase;
                Debug.Log($"{name}, sarı alanda {bestTarget.characterName} karakterini gördü! Yakalamak için koşuyor...");
            }
        }
    }

    // ---------- DEVRİYE (PATROL) ----------
    void HandlePatrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0 || patrolPoints[0] == null)
            return;

        Transform pt = patrolPoints[currentPointIndex];
        if (pt == null) return;

        bool reached = MoveTowardsPoint(pt.position, patrolSpeed, 0.25f);
        if (reached)
        {
            stateTimer += Time.deltaTime;
            if (stateTimer >= waitTimeAtPoint)
            {
                stateTimer = 0f;
                currentPointIndex = (currentPointIndex + 1) % patrolPoints.Length;
            }
        }
    }

    // ---------- KOVALAMA (CHASE: Düşman karaktere fiziksel olarak dokunana kadar yürür) ----------
    void HandleChase()
    {
        if (targetPlayer == null || !targetPlayer.isActiveAndEnabled || targetPlayer.IsDead ||
            targetPlayer.isHidden || Bush.IsPointInAnyBush(targetPlayer.WorldPosition))
        {
            currentState = State.Search;
            stateTimer = 0f;
            targetPlayer = null;
            return;
        }

        Vector3 playerPos = targetPlayer.WorldPosition;
        lastSeenPosition = playerPos;

        // Düşman karaktere fiziksel olarak dokunacak mesafeye (0.55m) gelene kadar kovalar
        bool touchingPlayer = MoveTowardsPoint(playerPos, chaseSpeed, attackRange);
        if (touchingPlayer)
        {
            currentState = State.Attack;
        }
    }

    // ---------- ARAMA (ÇİMENE GİRİNCE) ----------
    void HandleSearch()
    {
        bool reachedLastSeen = MoveTowardsPoint(lastSeenPosition, patrolSpeed, 0.35f);
        if (reachedLastSeen)
        {
            stateTimer += Time.deltaTime;
            transform.Rotate(0f, Mathf.Sin(stateTimer * 4f) * 70f * Time.deltaTime, 0f);

            if (stateTimer >= 2.2f)
            {
                stateTimer = 0f;
                currentState = State.Patrol;
            }
        }
    }

    // ---------- SALDIRI (ATTACK: Sadece düşman karaktere fiziksel olarak dokunduğunda vurur!) ----------
    void HandleAttack()
    {
        if (targetPlayer == null || !targetPlayer.isActiveAndEnabled || targetPlayer.IsDead ||
            targetPlayer.isHidden || Bush.IsPointInAnyBush(targetPlayer.WorldPosition))
        {
            currentState = State.Search;
            stateTimer = 0f;
            targetPlayer = null;
            return;
        }

        Vector3 playerPos = targetPlayer.WorldPosition;
        Vector3 lookDir = playerPos - transform.position;
        lookDir.y = 0f;
        float dist = lookDir.magnitude;

        if (lookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 12f);

        // Eğer karakter düşmandan uzaklaştıysa (dokunma mesafesinden çıktıysa) tekrar kovala!
        if (dist > attackRange + 0.15f)
        {
            currentState = State.Chase;
            return;
        }

        if (!isLunging && Time.time >= nextAttackTime)
        {
            nextAttackTime = Time.time + attackCooldown;
            if (anim != null) anim.SetTrigger("Attack");
            StartCoroutine(AttackTouchRoutine(targetPlayer));
        }
    }

    IEnumerator AttackTouchRoutine(PlayerCharacter victim)
    {
        isLunging = true;
        float duration = 0.22f;
        bool hitApplied = false;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float normalized = t / duration;
            currentLungeOffset = Mathf.Sin(normalized * Mathf.PI) * 0.45f;

            // Tam temas anında (ortada) karakter hâlâ dokunma mesafesindeyse ve gizlenmediyse hasar ver
            if (!hitApplied && normalized >= 0.45f)
            {
                hitApplied = true;
                if (victim != null && !victim.IsDead && !victim.isHidden && !Bush.IsPointInAnyBush(victim.WorldPosition))
                {
                    Vector3 d = victim.WorldPosition - transform.position;
                    d.y = 0f;
                    if (d.magnitude <= attackRange + 0.3f)
                    {
                        victim.TakeHit();
                    }
                }
            }
            yield return null;
        }

        currentLungeOffset = 0f;
        isLunging = false;
    }

    // ---------- ZEMİNDE ÜÇGEN GÖRÜŞ ALANI ÇİZİMİ (DOLU ÜÇGEN + ÇERÇEVE) ----------
    void SetupVisionConeRenderer()
    {
        var existing = transform.Find("VisionConeVisual");
        if (existing != null) Destroy(existing.gameObject);

        var coneObj = new GameObject("VisionConeVisual");
        coneObj.transform.SetParent(transform, false);
        coneObj.transform.localPosition = Vector3.zero;
        coneObj.transform.localRotation = Quaternion.identity;

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        var mat = new Material(shader);

        coneMeshFilter = coneObj.AddComponent<MeshFilter>();
        coneMeshRenderer = coneObj.AddComponent<MeshRenderer>();
        coneMeshRenderer.material = new Material(mat);
        coneMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        coneMeshRenderer.receiveShadows = false;
        coneMesh = new Mesh { name = "VisionConeMesh" };
        coneMeshFilter.mesh = coneMesh;

        coneLine = coneObj.AddComponent<LineRenderer>();
        coneLine.useWorldSpace = true;
        coneLine.loop = true;
        coneLine.startWidth = 0.06f;
        coneLine.endWidth = 0.06f;
        coneLine.material = new Material(mat);
        coneLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void UpdateVisionConeVisual()
    {
        if (!drawConeOnGround) return;
        if (coneLine == null || coneMesh == null) SetupVisionConeRenderer();

        bool alert = currentState == State.Chase || currentState == State.Attack;
        Color fillColor = alert ? new Color(1f, 0.15f, 0.15f, 0.35f) : new Color(1f, 0.9f, 0.15f, 0.24f);
        Color lineColor = alert ? new Color(1f, 0.1f, 0.1f, 0.95f) : new Color(1f, 0.85f, 0.1f, 0.85f);

        coneMeshRenderer.material.color = fillColor;
        coneLine.startColor = lineColor;
        coneLine.endColor = lineColor;

        int segments = 20;
        Vector3 originWorld = transform.position + Vector3.up * 0.08f;
        Vector3 fwd = GetForward();

        Vector3[] vertices = new Vector3[segments + 2];
        Color[] colors = new Color[segments + 2];
        int[] triangles = new int[segments * 3];

        coneLine.positionCount = segments + 2;
        coneLine.SetPosition(0, originWorld);
        vertices[0] = transform.InverseTransformPoint(originWorld);
        colors[0] = fillColor;

        float startAngle = -viewAngle * 0.5f;
        float step = viewAngle / segments;

        for (int i = 0; i <= segments; i++)
        {
            float ang = startAngle + step * i;
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * fwd;
            float rayDist = WallObstacle.RaycastWalls2D(originWorld, dir, viewDistance);
            Vector3 ptWorld = originWorld + dir * rayDist;

            coneLine.SetPosition(i + 1, ptWorld);
            vertices[i + 1] = transform.InverseTransformPoint(ptWorld);
            colors[i + 1] = fillColor;

            if (i < segments)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
        }

        coneMesh.Clear();
        coneMesh.vertices = vertices;
        coneMesh.colors = colors;
        coneMesh.triangles = triangles;
        coneMesh.RecalculateBounds();
    }

    // ---------- SCENE EKRANINDA DEVRİYE NOKTALARINI VE ÜÇGENİ HER ZAMAN GÖSTER ----------
    void OnDrawGizmos()
    {
        if (patrolPoints != null && patrolPoints.Length > 0)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                if (patrolPoints[i] == null) continue;
                Gizmos.DrawSphere(patrolPoints[i].position + Vector3.up * 0.2f, 0.35f);
                int next = (i + 1) % patrolPoints.Length;
                if (patrolPoints[next] != null)
                    Gizmos.DrawLine(patrolPoints[i].position + Vector3.up * 0.2f, patrolPoints[next].position + Vector3.up * 0.2f);
            }
        }

        Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.8f);
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        Vector3 fwd = GetForward();
        Vector3 leftDir = Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * fwd;
        Vector3 rightDir = Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * fwd;
        Gizmos.DrawRay(origin, leftDir * viewDistance);
        Gizmos.DrawRay(origin, rightDir * viewDistance);
        Gizmos.DrawLine(origin + leftDir * viewDistance, origin + rightDir * viewDistance);
    }
}
