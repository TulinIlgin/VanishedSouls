using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Sahnedeki duvar / engel bileşeni (Cube.007 ve ileride eklenecek tüm duvarlar için).
// Aria, Lumen ve Düşmanlar bu bileşene sahip objelerin içinden geçemez, etrafından dolanarak geçer
// ve düşmanlar bu duvarların arkasını göremez.
public class WallObstacle : MonoBehaviour
{
    public static readonly List<WallObstacle> AllWalls = new List<WallObstacle>();

    [Header("Duvar Kenar Boşluğu")]
    [Tooltip("Karakterlerin duvardan ne kadar açıktan dolanacağı (metre)")]
    public float defaultClearance = 0.75f;

    Vector2 centerXZ;
    Vector2 axisU = Vector2.right;
    Vector2 axisV = Vector2.up;
    float halfU = 0.5f;
    float halfV = 0.5f;
    float minY = -1f;
    float maxY = 5f;
    bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        AllWalls.Clear();
    }

    void OnEnable()
    {
        if (!AllWalls.Contains(this))
            AllWalls.Add(this);
        SetupWall();
    }

    void OnDisable()
    {
        AllWalls.Remove(this);
    }

    void Start()
    {
        SetupWall();
    }

    public void SetupWall()
    {
        var mf = GetComponent<MeshFilter>();
        var rend = GetComponent<Renderer>();

        // 1. Fiziksel Collider ekle/aktifleştir
        if (mf != null && mf.sharedMesh != null)
        {
            var mc = GetComponent<MeshCollider>();
            var bc = GetComponent<BoxCollider>();
            if (bc == null && mc == null)
                bc = gameObject.AddComponent<BoxCollider>();

            if (bc != null)
            {
                bc.center = mf.sharedMesh.bounds.center;
                bc.size = mf.sharedMesh.bounds.size;
                bc.enabled = true;
            }
            if (mc != null)
            {
                mc.enabled = true;
            }

            // 2. Aria (NavMeshAgent) için NavMeshObstacle (Carving) ekle
            var navObs = GetComponent<NavMeshObstacle>();
            if (navObs == null) navObs = gameObject.AddComponent<NavMeshObstacle>();
            navObs.shape = NavMeshObstacleShape.Box;
            navObs.center = mf.sharedMesh.bounds.center;
            navObs.size = mf.sharedMesh.bounds.size;
            navObs.carving = true;
            navObs.carveOnlyStationary = false;
            navObs.enabled = true;
        }

        RecalculateFootprint(mf, rend);
        initialized = true;
    }

    public void RecalculateFootprint(MeshFilter mf = null, Renderer rend = null)
    {
        if (mf == null) mf = GetComponent<MeshFilter>();
        if (rend == null) rend = GetComponent<Renderer>();

        if (mf != null && mf.sharedMesh != null)
        {
            Bounds lb = mf.sharedMesh.bounds;
            Vector3 worldCenter = transform.TransformPoint(lb.center);
            centerXZ = new Vector2(worldCenter.x, worldCenter.z);

            Vector3 ex = transform.TransformVector(new Vector3(lb.extents.x, 0f, 0f));
            Vector3 ey = transform.TransformVector(new Vector3(0f, lb.extents.y, 0f));
            Vector3 ez = transform.TransformVector(new Vector3(0f, 0f, lb.extents.z));

            Vector2 ex2 = new Vector2(ex.x, ex.z);
            Vector2 ey2 = new Vector2(ey.x, ey.z);
            Vector2 ez2 = new Vector2(ez.x, ez.z);

            // Yatay düzlemdeki (XZ) en uzun ekseni duvarın ana yönü (axisU) olarak seç
            Vector2 longest = ex2;
            if (ey2.sqrMagnitude > longest.sqrMagnitude) longest = ey2;
            if (ez2.sqrMagnitude > longest.sqrMagnitude) longest = ez2;

            axisU = longest.sqrMagnitude > 0.0001f ? longest.normalized : Vector2.right;
            axisV = new Vector2(-axisU.y, axisU.x);

            halfU = Mathf.Abs(Vector2.Dot(ex2, axisU)) + Mathf.Abs(Vector2.Dot(ey2, axisU)) + Mathf.Abs(Vector2.Dot(ez2, axisU));
            halfV = Mathf.Abs(Vector2.Dot(ex2, axisV)) + Mathf.Abs(Vector2.Dot(ey2, axisV)) + Mathf.Abs(Vector2.Dot(ez2, axisV));

            float wy = Mathf.Abs(ex.y) + Mathf.Abs(ey.y) + Mathf.Abs(ez.y);
            minY = worldCenter.y - wy;
            maxY = worldCenter.y + wy;
            return;
        }

        if (rend != null)
        {
            Bounds wb = rend.bounds;
            centerXZ = new Vector2(wb.center.x, wb.center.z);
            axisU = Vector2.right;
            axisV = Vector2.up;
            halfU = wb.extents.x;
            halfV = wb.extents.z;
            minY = wb.min.y;
            maxY = wb.max.y;
        }
    }

    Vector2 WorldToLocal2D(Vector3 worldPos)
    {
        Vector2 d = new Vector2(worldPos.x - centerXZ.x, worldPos.z - centerXZ.y);
        return new Vector2(Vector2.Dot(d, axisU), Vector2.Dot(d, axisV));
    }

    Vector3 LocalToWorld3D(float u, float v, float y)
    {
        Vector2 w = centerXZ + axisU * u + axisV * v;
        return new Vector3(w.x, y, w.y);
    }

    // 2B doğru parçası [-hu, +hu] x [-hv, +hv] dikdörtgenini kesiyor mu?
    bool SegmentIntersectsLocalBox(Vector2 a, Vector2 b, float hu, float hv)
    {
        Vector2 d = b - a;
        float tMin = 0f;
        float tMax = 1f;

        if (!ClipAxis(-d.x, a.x + hu, ref tMin, ref tMax)) return false;
        if (!ClipAxis(d.x, hu - a.x, ref tMin, ref tMax)) return false;
        if (!ClipAxis(-d.y, a.y + hv, ref tMin, ref tMax)) return false;
        if (!ClipAxis(d.y, hv - a.y, ref tMin, ref tMax)) return false;

        return tMax >= tMin;
    }

    static bool ClipAxis(float p, float q, ref float tMin, ref float tMax)
    {
        if (Mathf.Abs(p) < 1e-6f)
            return q >= 0f;

        float r = q / p;
        if (p < 0f)
        {
            if (r > tMax) return false;
            if (r > tMin) tMin = r;
        }
        else
        {
            if (r < tMin) return false;
            if (r < tMax) tMax = r;
        }
        return true;
    }

    public bool IntersectsSegment(Vector3 worldA, Vector3 worldB, float margin)
    {
        if (!initialized) SetupWall();
        Vector2 a = WorldToLocal2D(worldA);
        Vector2 b = WorldToLocal2D(worldB);

        float hu = halfU + margin;
        float hv = halfV + margin;

        // Başlangıç noktası marjın biraz içindeyse dışarı izdüşürerek kontrol et
        if (Mathf.Abs(a.x) < hu && Mathf.Abs(a.y) < hv)
        {
            float dx = hu - Mathf.Abs(a.x);
            float dy = hv - Mathf.Abs(a.y);
            if (dx < dy)
                a.x = Mathf.Sign(a.x == 0f ? 1f : a.x) * (hu + 0.02f);
            else
                a.y = Mathf.Sign(a.y == 0f ? 1f : a.y) * (hv + 0.02f);
        }

        return SegmentIntersectsLocalBox(a, b, hu, hv);
    }

    // Bu duvarın etrafından dolaşan en kısa köşe rotasını (1 veya 2 köşeli) hesaplar
    bool TryGetCornerRouteAroundWall(Vector3 fromPos, Vector3 goalPos, float clearance, out Vector3 firstCorner, out Vector3 secondCorner, out int cornerCount)
    {
        firstCorner = goalPos;
        secondCorner = goalPos;
        cornerCount = 0;

        float checkMargin = Mathf.Max(0.18f, clearance * 0.45f);
        float cornerMargin = clearance + 0.45f;

        float hu = halfU + cornerMargin;
        float hv = halfV + cornerMargin;
        float y = fromPos.y;

        // Dikdörtgenin 4 dış köşesi (saat yönünde sıralı)
        Vector3[] corners = new Vector3[4]
        {
            LocalToWorld3D(+hu, +hv, y),
            LocalToWorld3D(-hu, +hv, y),
            LocalToWorld3D(-hu, -hv, y),
            LocalToWorld3D(+hu, -hv, y)
        };

        float bestCost = float.MaxValue;

        // 1. Tek köşeli rotaları dene: fromPos -> C[i] -> goalPos
        for (int i = 0; i < 4; i++)
        {
            if (IntersectsSegment(fromPos, corners[i], checkMargin)) continue;
            if (IntersectsSegment(corners[i], goalPos, checkMargin)) continue;

            float dToCorner = Vector3.Distance(fromPos, corners[i]);
            float cost = dToCorner + Vector3.Distance(corners[i], goalPos);
            if (cost < bestCost)
            {
                bestCost = cost;
                firstCorner = corners[i];
                cornerCount = 1;
            }
        }

        // 2. İki köşeli (kenar boyu) rotaları dene: fromPos -> C[i] -> C[j] -> goalPos
        for (int i = 0; i < 4; i++)
        {
            if (IntersectsSegment(fromPos, corners[i], checkMargin)) continue;

            for (int step = -1; step <= 1; step += 2)
            {
                int j = (i + step + 4) % 4;
                if (IntersectsSegment(corners[j], goalPos, checkMargin)) continue;

                float d1 = Vector3.Distance(fromPos, corners[i]);
                float d2 = Vector3.Distance(corners[i], corners[j]);
                float d3 = Vector3.Distance(corners[j], goalPos);
                float cost = d1 + d2 + d3;

                if (cost < bestCost)
                {
                    bestCost = cost;
                    // Eğer ilk köşeye zaten çok yakınsak doğrudan ikinci köşeyi hedefle
                    if (d1 <= 0.55f)
                    {
                        firstCorner = corners[j];
                        cornerCount = 1;
                    }
                    else
                    {
                        firstCorner = corners[i];
                        secondCorner = corners[j];
                        cornerCount = 2;
                    }
                }
            }
        }

        // Eğer başlangıç noktası duvara çok yakınsa en yakın görünür köşeye yönlendir
        if (cornerCount == 0)
        {
            for (int i = 0; i < 4; i++)
            {
                float d = Vector3.Distance(fromPos, corners[i]);
                if (d > 0.4f && d < bestCost)
                {
                    bestCost = d;
                    firstCorner = corners[i];
                    cornerCount = 1;
                }
            }
        }

        return cornerCount > 0;
    }

    // ---------- STATİK YARDIMCI FONKSİYONLAR (Aria, Lumen ve EnemyAI Kullanır) ----------

    public static void EnsureAllWallsInitialized()
    {
        if (AllWalls.Count == 0)
        {
            foreach (var w in FindObjectsByType<WallObstacle>(FindObjectsSortMode.None))
            {
                if (w != null && !AllWalls.Contains(w))
                {
                    AllWalls.Add(w);
                    w.SetupWall();
                }
            }
        }
    }

    // İki nokta arasında duvar var mı? (Görüş alanı ve hareket kontrolü için)
    public static bool IsSegmentBlocked(Vector3 fromPos, Vector3 toPos, float clearance = 0.1f)
    {
        EnsureAllWallsInitialized();
        for (int i = 0; i < AllWalls.Count; i++)
        {
            var w = AllWalls[i];
            if (w == null || !w.isActiveAndEnabled) continue;
            if (w.IntersectsSegment(fromPos, toPos, clearance))
                return true;
        }
        return false;
    }

    // Karakterin hedefe giderken önüne duvar çıkarsa etrafından dolanacağı sıradaki köşe noktasını verir
    public static Vector3 GetSteeredTarget(Vector3 fromPos, Vector3 goalPos, float clearance = 0.75f)
    {
        EnsureAllWallsInitialized();

        WallObstacle closestBlockingWall = null;
        float closestDistSq = float.MaxValue;

        for (int i = 0; i < AllWalls.Count; i++)
        {
            var w = AllWalls[i];
            if (w == null || !w.isActiveAndEnabled) continue;

            if (w.IntersectsSegment(fromPos, goalPos, clearance))
            {
                Vector2 d = new Vector2(fromPos.x - w.centerXZ.x, fromPos.z - w.centerXZ.y);
                float dSq = d.sqrMagnitude;
                if (dSq < closestDistSq)
                {
                    closestDistSq = dSq;
                    closestBlockingWall = w;
                }
            }
        }

        if (closestBlockingWall != null &&
            closestBlockingWall.TryGetCornerRouteAroundWall(fromPos, goalPos, clearance, out Vector3 c1, out _, out int count) &&
            count > 0)
        {
            return c1;
        }

        return goalPos;
    }

    // Lumen'in yerdeki sarı yol çizgisinin duvarın etrafından dolanarak çizilmesi için rota noktalarını doldurur
    public static List<Vector3> BuildDetourPath(Vector3 fromPos, Vector3 goalPos, float clearance = 0.75f)
    {
        EnsureAllWallsInitialized();
        var path = new List<Vector3>(4) { fromPos + Vector3.up * 0.08f };

        for (int i = 0; i < AllWalls.Count; i++)
        {
            var w = AllWalls[i];
            if (w == null || !w.isActiveAndEnabled) continue;

            if (w.IntersectsSegment(fromPos, goalPos, clearance) &&
                w.TryGetCornerRouteAroundWall(fromPos, goalPos, clearance, out Vector3 c1, out Vector3 c2, out int count))
            {
                path.Add(c1 + Vector3.up * 0.08f);
                if (count == 2)
                    path.Add(c2 + Vector3.up * 0.08f);
                break;
            }
        }

        path.Add(goalPos + Vector3.up * 0.08f);
        return path;
    }

    // Karakterin duvarın içine girmesini fiziksel olarak engeller (duvar dışına iter)
    public static Vector3 PushOutOfWalls(Vector3 pos, float radius = 0.55f)
    {
        EnsureAllWallsInitialized();
        for (int i = 0; i < AllWalls.Count; i++)
        {
            var w = AllWalls[i];
            if (w == null || !w.isActiveAndEnabled) continue;
            if (!w.initialized) w.SetupWall();

            Vector2 local = w.WorldToLocal2D(pos);
            float hu = w.halfU + radius;
            float hv = w.halfV + radius;

            if (Mathf.Abs(local.x) < hu && Mathf.Abs(local.y) < hv)
            {
                float penU = hu - Mathf.Abs(local.x);
                float penV = hv - Mathf.Abs(local.y);

                if (penU < penV)
                    local.x = Mathf.Sign(local.x == 0f ? 1f : local.x) * hu;
                else
                    local.y = Mathf.Sign(local.y == 0f ? 1f : local.y) * hv;

                pos = w.LocalToWorld3D(local.x, local.y, pos.y);
            }
        }
        return pos;
    }

    // Düşmanın üçgen görüş konisi ışınlarının duvara çarpıp durması için 2B Raycast
    public static float RaycastWalls2D(Vector3 origin, Vector3 dir, float maxDist)
    {
        EnsureAllWallsInitialized();
        float bestDist = maxDist;
        Vector3 endPt = origin + dir * maxDist;

        for (int i = 0; i < AllWalls.Count; i++)
        {
            var w = AllWalls[i];
            if (w == null || !w.isActiveAndEnabled) continue;
            if (!w.initialized) w.SetupWall();

            Vector2 a = w.WorldToLocal2D(origin);
            Vector2 b = w.WorldToLocal2D(endPt);
            Vector2 d = b - a;

            float tMin = 0f;
            float tMax = 1f;
            if (ClipAxis(-d.x, a.x + w.halfU, ref tMin, ref tMax) &&
                ClipAxis(d.x, w.halfU - a.x, ref tMin, ref tMax) &&
                ClipAxis(-d.y, a.y + w.halfV, ref tMin, ref tMax) &&
                ClipAxis(d.y, w.halfV - a.y, ref tMin, ref tMax) &&
                tMax >= tMin && tMin >= 0f)
            {
                float hitDist = tMin * maxDist;
                if (hitDist < bestDist)
                    bestDist = hitDist;
            }
        }

        return bestDist;
    }

    void OnDrawGizmosSelected()
    {
        if (!initialized) RecalculateFootprint();
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.85f);
        float y = (minY + maxY) * 0.5f;
        Vector3 c0 = LocalToWorld3D(+halfU, +halfV, y);
        Vector3 c1 = LocalToWorld3D(-halfU, +halfV, y);
        Vector3 c2 = LocalToWorld3D(-halfU, -halfV, y);
        Vector3 c3 = LocalToWorld3D(+halfU, -halfV, y);
        Gizmos.DrawLine(c0, c1);
        Gizmos.DrawLine(c1, c2);
        Gizmos.DrawLine(c2, c3);
        Gizmos.DrawLine(c3, c0);
    }
}
