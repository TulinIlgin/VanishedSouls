using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public class EnemyAI : Enemy
{
    public enum State { Patrol, Chase, Search, Attack }

    [Header("Devriye Ayarları (Noktalar)")]
    [Tooltip("Düşmanın sırayla gezeceği devriye noktaları (Boşsa sahnede Patrol1/Patrol2 aranır, yoksa olduğu yerde bekler)")]
    public Transform[] patrolPoints;
    public float patrolSpeed = 2.0f;
    public float waitTimeAtPoint = 1.2f;

    [Header("Üçgen Görüş Alanı (FOV)")]
    [Range(20f, 160f)] public float viewAngle = 70f;
    public float viewDistance = 8.5f;
    public float eyeHeight = 1.3f;
    public bool drawConeOnGround = true;

    [Header("Saldırı ve Kovalama Ayarları")]
    public float chaseSpeed = 2.5f;
    [Tooltip("Düşmanın saldırmaya başlayabileceği maksimum mesafe")]
    public float attackRange = 8.5f;
    [Tooltip("İki saldırı arasındaki bekleme süresi (saniye)")]
    public float attackCooldown = 1.5f;
    [Tooltip("Düşman bizi gördükten sonra saldırı moduna geçmeden önceki hata payı süresi (saniye)")]
    public float detectionGraceTime = 0.1f;

    [Header("Görsel Model (Opsiyonel)")]
    [Tooltip("Script doğrudan yeni karakterin üzerindeyse burayı boş bırakabilirsiniz.")]
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

    public Animator anim;
    PlayerCharacter targetPlayer;
    PlayerCharacter pendingPlayer;
    float currentSeenDuration;
    Vector3 lastSeenPosition;
    int currentPointIndex;
    float stateTimer;
    float nextAttackTime;
    bool isAttacking;
    float distractedByStoneUntil;
    float baseGroundY;

    LineRenderer coneLine;
    MeshFilter coneMeshFilter;
    MeshRenderer coneMeshRenderer;
    Mesh coneMesh;
    readonly List<BoundPart> boundParts = new List<BoundPart>();

    // static bool navMeshRebuiltInPlayMode;
    // RuntimeNavMeshBuilder hatalarını önlemek için Play Mode'da otomatik NavMesh bake kaldırıldı.
    // NavMesh editörde Tools -> Vanished Souls -> Sahneyi ve NavMesh'i Güncelle menüsünden bake edilir.

    static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null || layer < 0) return;
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    public static void EnsureSceneConfigured()
    {
        int ignoreRaycastLayer = 2; // Ignore Raycast
        int charLayer = LayerMask.NameToLayer("Character");
        if (charLayer < 0) charLayer = 6;
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer < 0) enemyLayer = 8;

        // 1. Eski test objelerini kapat
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

        // 2. Çimen objelerine Bush, Cube.007'ye WallObstacle kur
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
        }

        // 3. Aria'da PlayerCharacter olduğundan emin ol
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

        // 4. Lumen'i hazırla (Varsa layer'ını Character yap ve final noktasını bağla)
        var existingLumen = FindAnyObjectByType<LumenController>(FindObjectsInactive.Include);
        if (existingLumen != null)
        {
            existingLumen.gameObject.SetActive(true);
            SetLayerRecursively(existingLumen.transform, charLayer);

            var pc = existingLumen.GetComponent<PlayerCharacter>();
            if (pc == null) pc = existingLumen.gameObject.AddComponent<PlayerCharacter>();
            pc.characterName = "Lumen";
            pc.maxHealth = 1;
            pc.diePermanentlyOnHit = true;

            GameObject finalObj = GameObject.Find("LumenFinalNoktasi");
            if (finalObj != null)
            {
                if (existingLumen.finalPoint == null) existingLumen.finalPoint = finalObj.transform;
                FinishLine.EnsureOnFinalPoint(finalObj.transform);
            }
        }

        // 5. GameMenuManager ve CameraFollow
        if (FindAnyObjectByType<GameMenuManager>() == null)
        {
            var menuHost = GameObject.Find("GameMenuManager");
            if (menuHost == null) menuHost = new GameObject("GameMenuManager");
            if (menuHost.GetComponent<GameMenuManager>() == null)
                menuHost.AddComponent<GameMenuManager>();
        }

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            var camFollow = mainCam.GetComponent<CameraFollow>();
            if (camFollow == null) camFollow = mainCam.gameObject.AddComponent<CameraFollow>();
            if (aria != null && camFollow.target == null)
                camFollow.target = aria.transform;
        }
    }

    public static void RebuildSceneNavMesh()
    {
        EnsureSceneConfigured();

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
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }
    }

    void Awake()
    {
        // 1. Düşman katmanını otomatik ayarla (Layer 8: Enemy)
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer >= 0)
        {
            SetLayerRecursively(transform, enemyLayer);
        }

        // 2. Karakterde veya alt objelerinde Collider yoksa otomatik bir CapsuleCollider ekle
        var existingCol = GetComponentInChildren<Collider>();
        if (existingCol == null)
        {
            var cap = gameObject.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 1.0f, 0f);
            cap.height = 2.0f;
            cap.radius = 0.5f;
        }

        // 3. NavMeshAgent varsa devre dışı bırak (özel hareket ve duvar dolanma sistemi kullanıyoruz)
        var nav = GetComponent<NavMeshAgent>();
        if (nav != null) nav.enabled = false;

        if (attackRange < viewDistance) attackRange = viewDistance;
        if (chaseSpeed > 2.8f) chaseSpeed = 2.5f;

        // 4. Animator bileşenini kendinde veya alt objelerinde otomatik bul
        anim = GetComponentInChildren<Animator>();
        if (anim != null && anim.gameObject != gameObject)
        {
            var bridge = anim.GetComponent<EnemyAnimationEvents>();
            if (bridge == null)
            {
                anim.gameObject.AddComponent<EnemyAnimationEvents>();
            }
        }
        baseGroundY = transform.position.y;

        // 5. Eğer harici bir görsel model atanmışsa bağla (Doğrudan child ise gerekmez)
        if (visualModel != null && visualModel != transform && !visualModel.IsChildOf(transform))
        {
            BindVisualParts();
        }

        // 6. Zemindeki üçgen görüş alanını kur
        if (drawConeOnGround)
            SetupVisionConeRenderer();
    }

    public void BindVisualParts()
    {
        if (boundParts.Count > 0 || visualModel == null || visualModel == transform || visualModel.IsChildOf(transform)) return;
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
        if (part == null || part == transform || part.IsChildOf(transform)) return;
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

        // Eğer Inspector'dan patrol point atanmadıysa ve sahnede Patrol1/Patrol2 varsa onları bağla
        if (patrolPoints == null || patrolPoints.Length == 0 || patrolPoints[0] == null)
        {
            var p1 = GameObject.Find("Patrol1");
            var p2 = GameObject.Find("Patrol2");
            if (p1 != null && p2 != null)
            {
                patrolPoints = new Transform[] { p1.transform, p2.transform };
            }
        }
    }

    void Update()
    {
        if (dead)
        {
            if (anim != null) anim.SetBool("move", false);
            return;
        }

        // Saldırı kilidi: FinishAttack() gelene kadar karakter tamamen kilitlidir, hareket edemez!
        if (isAttacking)
        {
            if (anim != null) anim.SetBool("move", false);
            return;
        }

        LookForPlayers();
        UpdateVisionConeVisual();

        bool isMoving = false;

        switch (currentState)
        {
            case State.Patrol:
                isMoving = HandlePatrol();
                break;
            case State.Chase:
                isMoving = HandleChase();
                break;
            case State.Search:
                isMoving = HandleSearch();
                break;
            case State.Attack:
                HandleAttack();
                isMoving = false; // Saldırırken veya nişan alırken yerinde durur
                break;
        }

        // Kullanıcının animasyon parametresi:
        // move == true -> Hareket (Yürüme/Koşma)
        // move == false -> Idle (Bekleme)
        if (anim != null)
        {
            anim.SetBool("move", isMoving);
        }
    }

    void LateUpdate()
    {
        if (dead || boundParts.Count == 0) return;

        for (int i = 0; i < boundParts.Count; i++)
        {
            var bp = boundParts[i];
            if (bp.part == null || bp.part == transform) continue;
            bp.part.position = transform.position + transform.rotation * bp.localPosOffset;
            bp.part.rotation = transform.rotation * bp.localRotOffset;
        }
    }

    public override void Die()
    {
        if (dead) return;
        if (anim != null) anim.SetBool("move", false);
        for (int i = 0; i < boundParts.Count; i++)
        {
            if (boundParts[i].part != null && boundParts[i].part != transform)
                Destroy(boundParts[i].part.gameObject);
        }
        base.Die();
    }

    Vector3 GetForward()
    {
        Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        return f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
    }

    // ---------- HEDEF NOKTAYA DOĞRU AKICI YÜRÜME VE DÖNME ----------
    bool MoveTowardsPoint(Vector3 targetWorldPos, float speed, float stopDist)
    {
        Vector3 myPos = transform.position;
        Vector3 toGoal = targetWorldPos - myPos;
        toGoal.y = 0f;
        float distToGoal = toGoal.magnitude;

        if (distToGoal <= stopDist)
            return true; // Hedefe vardı

        // Önünde duvar varsa dolan
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
        return false; // Hâlâ hedefe doğru yürüyor
    }

    // ---------- GÖRÜŞ ALANI KONTROLÜ (ÇALI GİZLİLİK VE DUVAR ENGELİ) ----------
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

            // Çalı kontrolü (MOBA tarzı gizlilik: Karakter çimendeyse düşman onu tamamen pas geçer)
            if (player.isHidden || Bush.IsHiddenFromEnemy(player, this)) continue;

            // Duvar engeli kontrolü
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

            // Hata payı süresi dolunca saldırı durumuna geç
            if (currentSeenDuration >= detectionGraceTime)
            {
                Bush.MarkSpottedBeforeBush(this, bestTarget);
                targetPlayer = bestTarget;
                lastSeenPosition = bestTarget.WorldPosition;
                currentState = State.Attack;
            }
        }
        else
        {
            pendingPlayer = null;
            currentSeenDuration = 0f;
        }
    }

    // ---------- DEVRİYE (PATROL) ----------
    bool HandlePatrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0 || patrolPoints[0] == null)
            return false; // Nokta yoksa yerinde durur (Idle)

        Transform pt = patrolPoints[currentPointIndex];
        if (pt == null) return false;

        bool reached = MoveTowardsPoint(pt.position, patrolSpeed, 0.25f);
        if (reached)
        {
            stateTimer += Time.deltaTime;
            if (stateTimer >= waitTimeAtPoint)
            {
                stateTimer = 0f;
                currentPointIndex = (currentPointIndex + 1) % patrolPoints.Length;
            }
            return false; // Noktada bekliyor -> move = false (Idle)
        }
        return true; // Noktaya doğru yürüyor -> move = true (Hareket)
    }

    // ---------- KOVALAMA (CHASE) ----------
    bool HandleChase()
    {
        if (targetPlayer == null || !targetPlayer.isActiveAndEnabled || targetPlayer.IsDead ||
            Bush.IsHiddenFromEnemy(targetPlayer, this))
        {
            Bush.ClearSpottedState(this, targetPlayer);
            currentState = State.Search;
            stateTimer = 0f;
            targetPlayer = null;
            return false;
        }

        Vector3 playerPos = targetPlayer.WorldPosition;

        if (WallObstacle.IsSegmentBlocked(transform.position, playerPos, 0.05f))
        {
            Bush.ClearSpottedState(this, targetPlayer);
            currentState = State.Search;
            stateTimer = 0f;
            targetPlayer = null;
            return false;
        }

        lastSeenPosition = playerPos;
        bool inRange = MoveTowardsPoint(playerPos, chaseSpeed, attackRange);
        if (inRange)
        {
            currentState = State.Attack;
            return false;
        }
        return true; // Oyuncuya doğru hareket ediyor -> move = true
    }

    // ---------- TAŞ İLE DİKKAT DAĞITMA VE ARAMA ----------
    public static void NotifyStoneNoise(Vector3 stoneLandingPos, float hearingRadius = 20f)
    {
        EnemyAI closestEnemy = null;
        float closestDist = float.MaxValue;
        bool anyReacted = false;

        foreach (var enemy in FindObjectsByType<EnemyAI>())
        {
            if (enemy == null || !enemy.isActiveAndEnabled || enemy.dead) continue;

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
        isAttacking = false;
        Bush.ClearSpottedState(this);

        distractedByStoneUntil = Time.time + 5.5f;
        lastSeenPosition = new Vector3(stonePos.x, baseGroundY, stonePos.z);
        currentState = State.Search;
        stateTimer = -1.5f;
        Debug.Log($"{name} taş sesini duydu ve araştırıyor: {lastSeenPosition}");
    }

    // ---------- ARAMA (SEARCH) ----------
    bool HandleSearch()
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
            return false; // Hedefe vardı, etrafa bakınıyor -> move = false (Idle)
        }
        return true; // Hedefe doğru yürüyor -> move = true
    }

    // ---------- SALDIRI DURUMU (ATTACK) ----------
    void HandleAttack()
    {
        if (targetPlayer == null || !targetPlayer.isActiveAndEnabled || targetPlayer.IsDead ||
            Bush.IsHiddenFromEnemy(targetPlayer, this))
        {
            Bush.ClearSpottedState(this, targetPlayer);
            currentState = State.Search;
            stateTimer = 0f;
            targetPlayer = null;
            isAttacking = false;
            return;
        }

        Vector3 playerPos = targetPlayer.WorldPosition;
        if (WallObstacle.IsSegmentBlocked(transform.position, playerPos, 0.05f))
        {
            Bush.ClearSpottedState(this, targetPlayer);
            currentState = State.Search;
            stateTimer = 0f;
            targetPlayer = null;
            isAttacking = false;
            return;
        }

        Vector3 lookDir = playerPos - transform.position;
        lookDir.y = 0f;
        float dist = lookDir.magnitude;

        // Hedefe doğru yüzünü dön
        if (lookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 15f);

        // Hedef menzilden çok çıktıysa kovalamaya dön
        if (dist > Mathf.Max(attackRange, viewDistance) + 0.5f)
        {
            currentState = State.Chase;
            isAttacking = false;
            return;
        }

        // Saldırı zamanı geldiyse Attack Trigger'ını ateşle ve kilitle
        if (Time.time >= nextAttackTime)
        {
            isAttacking = true;
            nextAttackTime = Time.time + attackCooldown;

            if (anim != null)
            {
                anim.SetTrigger("Attack");
                anim.SetBool("move", false);
            }
            Debug.Log($"{name} saldırı animasyonuna girdi. FinishAttack gelene kadar hareket kilitlendi!");
        }
    }

    // =========================================================================
    // ANİMASYON EVENT FONKSİYONLARI (Animation Window üzerinden çağrılır)
    // =========================================================================

    /// <summary>
    /// Vuruş anı: Animasyonda okun fırlatıldığı / darbenin indiği kareye ekleyin.
    /// </summary>
    public void OnAttackHit()
    {
        PlayerCharacter victim = targetPlayer ?? pendingPlayer;
        if (victim != null && !victim.IsDead && !Bush.IsHiddenFromEnemy(victim, this))
        {
            if (!WallObstacle.IsSegmentBlocked(transform.position, victim.WorldPosition, 0.05f))
            {
                victim.TakeHit();
                Debug.Log($"{name} saldırı vuruşunu yaptı: {victim.characterName} vuruldu!");
            }
        }
    }

    /// <summary>
    /// Bitiş anı: Animasyonun EN SON karesine ekleyin. Bu çağrılana kadar karakter hareket edemez.
    /// </summary>
    public void FinishAttack()
    {
        isAttacking = false;
        Debug.Log($"{name} FinishAttack çağrıldı. Saldırı bitti, hareket kilidi açıldı.");

        // Hedef öldüyse veya kaybolduysa aramaya geç
        if (targetPlayer == null || targetPlayer.IsDead || Bush.IsHiddenFromEnemy(targetPlayer, this))
        {
            Bush.ClearSpottedState(this, targetPlayer);
            targetPlayer = null;
            currentState = State.Search;
            stateTimer = 0f;
        }
    }

    // ---------- ZEMİNDE ÜÇGEN GÖRÜŞ ALANI ÇİZİMİ ----------
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
