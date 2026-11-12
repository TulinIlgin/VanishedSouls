using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
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

    [Header("Uzak Mesafe Saldırı ve Kovalama Ayarları")]
    public float chaseSpeed = 2.5f;
    [Tooltip("Düşmanın uzaktan öldürebileceği mesafe (Üçgen görüş alanı mesafesi kadar)")]
    public float attackRange = 8.5f;
    public float attackCooldown = 0.8f;
    [Tooltip("Düşman bizi gördükten sonra öldürme moduna geçmeden önceki hata payı süresi (saniye)")]
    public float detectionGraceTime = 0.1f;

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
    PlayerCharacter pendingPlayer;
    float currentSeenDuration;
    Vector3 lastSeenPosition;
    int currentPointIndex;
    float stateTimer;
    float nextAttackTime;
    float distractedByStoneUntil;
    float baseGroundY;
    float currentLungeOffset;
    bool isLunging;

    LineRenderer coneLine;
    MeshFilter coneMeshFilter;
    MeshRenderer coneMeshRenderer;
    Mesh coneMesh;
    readonly List<BoundPart> boundParts = new List<BoundPart>();

    static bool navMeshRebuiltInPlayMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoConfigureSceneOnPlay()
    {
        navMeshRebuiltInPlayMode = false;
        EnsureSceneConfigured();
        if (!navMeshRebuiltInPlayMode)
        {
            navMeshRebuiltInPlayMode = true;
            RebuildSceneNavMesh();
        }
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null || layer < 0) return;
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    public static void EnsureSceneConfigured()
    {
        int ignoreRaycastLayer = 2; // Ignore Raycast (Çimenler, yer sarmaşıkları ve görsel çizgiler NavMesh'i engellemesin)
        int charLayer = LayerMask.NameToLayer("Character");
        if (charLayer < 0) charLayer = 6;
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer < 0) enemyLayer = 8;

        // 1. Eski test küpünü ("Cube") ve yanlışlıkla kopyalanmış "blockout (1)" objesini kapat
        foreach (var go in FindObjectsByType<GameObject>())
        {
            if (go == null) continue;
            if (go.name == "Cube" && go.transform.parent == null && go.GetComponent<BoxCollider>() != null)
            {
                go.SetActive(false);
            }
            else if (go.name == "blockout (1)")
            {
                go.SetActive(false);
            }
        }

        // 2. Çimen objelerine ("Sketchfab_model.003", "g2_0", "sketchfabGrass", "grasspatches", "Object_4.001", "Object_5*") Bush kur (Layer 2),
        //    "YerSarmasik" (zemin sarmaşıkları) ve taşları ("Sphere") Layer 2'ye al ki NavMesh zemini kapatmasın,
        //    "Cube.007" objesini de duvar (WallObstacle) olarak ayarla
        foreach (var t in FindObjectsByType<Transform>())
        {
            if (t == null) continue;
            string n = t.name;

            if (n == "Sketchfab_model.003" || n == "g2_0" || n == "sketchfabGrass" || n == "grasspatches" ||
                ((n == "Object_4.001" || n.StartsWith("Object_5")) && t.GetComponent<Renderer>() != null))
            {
                var bush = t.GetComponent<Bush>();
                if (bush == null) bush = t.gameObject.AddComponent<Bush>();
                bush.SetupGrassColliders();
            }
            else if (n == "Cube.007")
            {
                var wall = t.GetComponent<WallObstacle>();
                if (wall == null) wall = t.gameObject.AddComponent<WallObstacle>();
                wall.SetupWall();
            }
            else if (n == "YerSarmasik" || n == "VisionConeVisual" || n == "LumenPathLine" || n == "FinalPointVisual" || n == "FinishLineVisual")
            {
                SetLayerRecursively(t, ignoreRaycastLayer);
            }
            else if (n.StartsWith("Sphere") && t.GetComponent<Rigidbody>() != null)
            {
                t.gameObject.layer = ignoreRaycastLayer;
            }
            else if (n == "Enemy" || n.StartsWith("Enemy_Humainoid_Enemy"))
            {
                SetLayerRecursively(t, enemyLayer);
            }
        }

        // 3. Aria'da PlayerCharacter olduğundan ve Layer 6 (Character) katmanında olduğundan emin ol
        var aria = FindAnyObjectByType<AriaController>();
        if (aria != null)
        {
            SetLayerRecursively(aria.transform, charLayer);
            if (aria.GetComponent<PlayerCharacter>() == null)
            {
                var pc = aria.gameObject.AddComponent<PlayerCharacter>();
                pc.characterName = "Aria";
            }
        }

        // 4. Sarı karakteri (Sketchfab_model -> Object_2) Lumen olarak kur, Layer 6'ya al ve LumenFinalNoktasi oluştur
        Transform lumenVisual = null;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t == null) continue;
            if (t.name == "Sketchfab_model" && t.root.name != "blockout (1)")
            {
                t.gameObject.SetActive(true);
                SetLayerRecursively(t, charLayer);
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
        foreach (var ai in FindObjectsByType<EnemyAI>())
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
        foreach (var t in FindObjectsByType<Transform>())
        {
            if (t == null || t.root.name == "blockout (1)") continue;
            if (t.name == "Enemy_Humainoid_Enemy_0") mesh1 = t;
            else if (t.name == "Enemy_Humainoid_Enemy_0.001") mesh2 = t;
            else if (t.name == "Left_Eye") leftEye = t;
            else if (t.name == "Right_Eye") rightEye = t;
        }

        if (mesh1 != null)
            SetupOrUpdateEnemyHost("Dusman_1", mesh1, new Transform[] { leftEye, rightEye }, "Patrol1", "Patrol2");

        if (mesh2 != null)
            SetupOrUpdateEnemyHost("Dusman_2", mesh2, null, "Patrol3", "Patrol4");

        // 7. Arkadaşının UI panellerini ve butonlarını bağlayabileceği boş GameMenuManager objesini hazırla
        if (FindAnyObjectByType<GameMenuManager>() == null)
        {
            var menuHost = GameObject.Find("GameMenuManager");
            if (menuHost == null) menuHost = new GameObject("GameMenuManager");
            if (menuHost.GetComponent<GameMenuManager>() == null)
                menuHost.AddComponent<GameMenuManager>();
        }

        // 8. Main Camera'ya CameraFollow bileşenini ekle ve Aria'yı takip etmesini sağla
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            var camFollow = mainCam.GetComponent<CameraFollow>();
            if (camFollow == null) camFollow = mainCam.gameObject.AddComponent<CameraFollow>();
            if (aria != null && camFollow.target == null)
                camFollow.target = aria.transform;
        }
    }

    // ---------- SAHNEDEKİ TÜM YENİ HARİTA OBJELERİYLE NAVMESH'İ GÜNCELLE ----------
    public static void RebuildSceneNavMesh()
    {
        EnsureSceneConfigured();

        // Karakterleri (Layer 6), Düşmanları (Layer 8) ve Çimen/YerSarmaşık/Görsel Çizgileri (Layer 2: Ignore Raycast)
        // NavMesh bake işleminden hariç tut ki karakterlerin altı ve çimenlerin içi her zaman yürünebilir (walkable) kalsın!
        int excludedLayers = (1 << 2) | (1 << 6) | (1 << 8);
        int bakeLayerMask = ~excludedLayers;

        var surfaces = FindObjectsByType<NavMeshSurface>();
        foreach (var surface in surfaces)
        {
            if (surface == null || !surface.gameObject.activeInHierarchy) continue;
            if (surface.gameObject.name == "blockout (1)")
            {
                surface.enabled = false;
                continue;
            }

            surface.collectObjects = CollectObjects.All;
            surface.layerMask = bakeLayerMask;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.BuildNavMesh();
        }
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
            finalObj.transform.position = new Vector3(-23.5f, footPos.y, 17.5f);
        }
        lumenCtrl.finalPoint = finalObj.transform;
        FinishLine.EnsureOnFinalPoint(finalObj.transform);

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
        ai.attackRange = Mathf.Max(ai.viewDistance, 8.5f);
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
        if (attackRange < viewDistance) attackRange = viewDistance;
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

    bool MoveTowardsPoint(Vector3 targetWorldPos, float speed, float stopDist)
    {
        Vector3 myPos = transform.position;
        Vector3 toGoal = targetWorldPos - myPos;
        toGoal.y = 0f;
        float distToGoal = toGoal.magnitude;

        if (distToGoal <= stopDist)
            return true;
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

    void LookForPlayers()
    {
        if (PlayerCharacter.AllPlayers.Count == 0)
        {
            foreach (var pc in FindObjectsByType<PlayerCharacter>())
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

            // KURAL: Karakter düşman görmeden (veya 0.1sn hata payı dolmadan) çalıya girdiyse,
            // düşman dibinden geçse bile onu ASLA göremez!
            // Ama düşman bizi 0.1sn+ gördükten sonra çalıya girdiysek IsHiddenFromEnemy false döner ve yakalar!
            if (Bush.IsHiddenFromEnemy(player, this)) continue;

            if (WallObstacle.IsSegmentBlocked(myPos, playerPos, 0.05f)) continue;

            Vector3 toPlayer = playerPos - myPos;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;

            if (dist > viewDistance) continue;

            float angle = dist > 0.05f ? Vector3.Angle(fwd, toPlayer.normalized) : 0f;
            bool insideTriangle = angle <= (viewAngle * 0.5f + 2f);
            bool touchingBodyOutsideBush = dist <= 0.55f;

            if (Time.time < distractedByStoneUntil && !touchingBodyOutsideBush)
                continue;

            if (!insideTriangle && !touchingBodyOutsideBush) continue;

            if (dist < bestDist)
            {
                bestDist = dist;
                bestTarget = player;
            }
        }

        if (bestTarget != null)
        {
            if (pendingPlayer != bestTarget)
            {
                pendingPlayer = bestTarget;
                currentSeenDuration = Bush.WasSpottedBeforeEnteringBush(this, bestTarget) ? detectionGraceTime : 0f;
            }

            currentSeenDuration += Time.deltaTime;

            if (currentSeenDuration >= detectionGraceTime)
            {
                Bush.MarkSpottedBeforeBush(this, bestTarget);
                targetPlayer = bestTarget;
                lastSeenPosition = bestTarget.WorldPosition;
                currentState = State.Attack;
                Debug.Log($"{name}, üçgen görüş alanında {bestTarget.characterName} karakterini fark etti ve uzaktan saldırıyor!");
            }
        }
        else
        {
            pendingPlayer = null;
            currentSeenDuration = 0f;
        }
    }

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

    void HandleChase()
    {
        if (targetPlayer == null || !targetPlayer.isActiveAndEnabled || targetPlayer.IsDead ||
            Bush.IsHiddenFromEnemy(targetPlayer, this))
        {
            Bush.ClearSpottedState(this, targetPlayer);
            currentState = State.Search;
            stateTimer = 0f;
            targetPlayer = null;
            return;
        }

        Vector3 playerPos = targetPlayer.WorldPosition;

        if (WallObstacle.IsSegmentBlocked(transform.position, playerPos, 0.05f))
        {
            Bush.ClearSpottedState(this, targetPlayer);
            currentState = State.Search;
            stateTimer = 0f;
            targetPlayer = null;
            return;
        }

        lastSeenPosition = playerPos;
        bool inRange = MoveTowardsPoint(playerPos, chaseSpeed, attackRange);
        if (inRange)
        {
            currentState = State.Attack;
        }
    }

    public static void NotifyStoneNoise(Vector3 stoneLandingPos, float hearingRadius = 20f)
    {
        EnemyAI closestEnemy = null;
        float closestDist = float.MaxValue;
        bool anyReacted = false;

        foreach (var enemy in FindObjectsByType<EnemyAI>())
        {
            if (enemy == null || !enemy.isActiveAndEnabled || enemy.dead) continue;
           
            if (enemy.visualModel == null && enemy.gameObject.name == "Enemy") continue;

            float d = Vector2.Distance(
                new Vector2(enemy.transform.position.x, enemy.transform.position.z),
                new Vector2(stoneLandingPos.x, stoneLandingPos.z));

            if (d < closestDist)
            {
                closestDist = d;
                closestEnemy = enemy;
            }

            if (d <= hearingRadius)
            {
                enemy.InvestigateStone(stoneLandingPos);
                anyReacted = true;
            }
        }
        if (!anyReacted && closestEnemy != null)
        {
            closestEnemy.InvestigateStone(stoneLandingPos);
        }
    }

    public void InvestigateStone(Vector3 stonePos)
    {
        if (dead) return;

        targetPlayer = null;
        pendingPlayer = null;
        currentSeenDuration = 0f;
        Bush.ClearSpottedState(this);

        distractedByStoneUntil = Time.time + 5.5f;
        lastSeenPosition = new Vector3(stonePos.x, baseGroundY, stonePos.z);
        currentState = State.Search;
        stateTimer = -1.5f;
        Debug.Log($"{name} taşın düştüğü sesi duydu ve {lastSeenPosition} noktasına doğru gidiyor!");
    }

    void HandleSearch()
    {
        bool reachedLastSeen = MoveTowardsPoint(lastSeenPosition, patrolSpeed * 1.15f, 0.35f);
        if (reachedLastSeen)
        {
            stateTimer += Time.deltaTime;
            transform.Rotate(0f, Mathf.Sin(stateTimer * 4f) * 70f * Time.deltaTime, 0f);

            if (stateTimer >= 2.2f)
            {
                stateTimer = 0f;
                distractedByStoneUntil = 0f;
                Bush.ClearSpottedState(this);
                currentState = State.Patrol;
            }
        }
    }

    void HandleAttack()
    {
        if (targetPlayer == null || !targetPlayer.isActiveAndEnabled || targetPlayer.IsDead ||
            Bush.IsHiddenFromEnemy(targetPlayer, this))
        {
            Bush.ClearSpottedState(this, targetPlayer);
            currentState = State.Search;
            stateTimer = 0f;
            targetPlayer = null;
            return;
        }

        Vector3 playerPos = targetPlayer.WorldPosition;
        if (WallObstacle.IsSegmentBlocked(transform.position, playerPos, 0.05f))
        {
            Bush.ClearSpottedState(this, targetPlayer);
            currentState = State.Search;
            stateTimer = 0f;
            targetPlayer = null;
            return;
        }

        Vector3 lookDir = playerPos - transform.position;
        lookDir.y = 0f;
        float dist = lookDir.magnitude;

        if (lookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 15f);

        if (dist > Mathf.Max(attackRange, viewDistance) + 0.5f)
        {
            currentState = State.Chase;
            return;
        }

        if (!isLunging && Time.time >= nextAttackTime)
        {
            nextAttackTime = Time.time + attackCooldown;
            if (anim != null) anim.SetTrigger("Attack");
            StartCoroutine(RangedAttackRoutine(targetPlayer));
        }
    }

    IEnumerator RangedAttackRoutine(PlayerCharacter victim)
    {
        isLunging = true;

        // uzak öldür
        if (victim != null && !victim.IsDead && !Bush.IsHiddenFromEnemy(victim, this))
        {
            victim.TakeHit();
        }

        float duration = 0.18f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float normalized = t / duration;
            currentLungeOffset = Mathf.Sin(normalized * Mathf.PI) * 0.25f;
            yield return null;
        }

        currentLungeOffset = 0f;
        isLunging = false;
    }

    static Material CreateConeMaterial()
    {
        var shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        var mat = new Material(shader);
        mat.hideFlags = HideFlags.HideAndDontSave;
    
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
        if (mat.HasProperty("_ZTest")) mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        mat.renderQueue = 3200;
        return mat;
    }

    void SetupVisionConeRenderer()
    {
        var existing = transform.Find("VisionConeVisual");
        if (existing != null) Destroy(existing.gameObject);

        var coneObj = new GameObject("VisionConeVisual");
        coneObj.layer = 0; 
        coneObj.transform.SetParent(transform, false);
        coneObj.transform.localPosition = Vector3.zero;
        coneObj.transform.localRotation = Quaternion.identity;

        coneMeshFilter = coneObj.AddComponent<MeshFilter>();
        coneMeshRenderer = coneObj.AddComponent<MeshRenderer>();
        coneMeshRenderer.material = CreateConeMaterial();
        coneMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        coneMeshRenderer.receiveShadows = false;
        coneMesh = new Mesh { name = "VisionConeMesh" };
        coneMeshFilter.mesh = coneMesh;

        coneLine = coneObj.AddComponent<LineRenderer>();
        coneLine.useWorldSpace = true;
        coneLine.loop = true;
        coneLine.startWidth = 0.08f;
        coneLine.endWidth = 0.08f;
        coneLine.material = CreateConeMaterial();
        coneLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void UpdateVisionConeVisual()
    {
        if (!drawConeOnGround) return;
        if (coneLine == null || coneMesh == null || coneMeshRenderer == null) SetupVisionConeRenderer();

        bool alert = currentState == State.Chase || currentState == State.Attack;
        Color fillColor = alert ? new Color(1f, 0.15f, 0.15f, 0.42f) : new Color(1f, 0.9f, 0.15f, 0.32f);
        Color lineColor = alert ? new Color(1f, 0.1f, 0.1f, 0.98f) : new Color(1f, 0.88f, 0.1f, 0.92f);

        coneMeshRenderer.material.color = fillColor;
        if (coneMeshRenderer.material.HasProperty("_Color"))
            coneMeshRenderer.material.SetColor("_Color", fillColor);
        coneLine.startColor = lineColor;
        coneLine.endColor = lineColor;
        if (coneLine.material.HasProperty("_Color"))
            coneLine.material.SetColor("_Color", lineColor);

        int segments = 20;
        float coneY = Mathf.Max(transform.position.y + 0.38f, 0.38f);
        Vector3 originWorld = new Vector3(transform.position.x, coneY, transform.position.z);
        Vector3 fwd = GetForward();

        Vector3[] vertices = new Vector3[segments + 2];
        Color[] colors = new Color[segments + 2];
        int[] triangles = new int[segments * 6];

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
            ptWorld.y = coneY;

            coneLine.SetPosition(i + 1, ptWorld);
            vertices[i + 1] = transform.InverseTransformPoint(ptWorld);
            colors[i + 1] = fillColor;

            if (i < segments)
            {
                int triIdx = i * 6;
                triangles[triIdx] = 0;
                triangles[triIdx + 1] = i + 1;
                triangles[triIdx + 2] = i + 2;

                triangles[triIdx + 3] = 0;
                triangles[triIdx + 4] = i + 2;
                triangles[triIdx + 5] = i + 1;
            }
        }

        coneMesh.Clear();
        coneMesh.vertices = vertices;
        coneMesh.colors = colors;
        coneMesh.triangles = triangles;
        coneMesh.RecalculateBounds();
    }

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
