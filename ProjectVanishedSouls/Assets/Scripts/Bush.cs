using System.Collections.Generic;
using UnityEngine;

// Çimen / çalı gizlenme alanı. İçine giren PlayerCharacter (Aria, Lumen vb.) düşmanlara görünmez olur.
public class Bush : MonoBehaviour
{
    public static readonly List<Bush> AllBushes = new List<Bush>();

    // Ignore Raycast katmanı (Layer 2): Tıklamaları engellemez ama fizik taraması ile gerçek çimen üçgenlerini yakalar
    const int GrassLayer = 2;

    [Tooltip("Çimenlerin arasındaki boşlukları da kapsama yarıçapı (metre)")]
    public float stealthRadius = 1.65f;

    static bool collidersConfigured;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        AllBushes.Clear();
        collidersConfigured = false;
    }

    void OnEnable()
    {
        if (!AllBushes.Contains(this))
            AllBushes.Add(this);
        SetupGrassColliders();
    }

    void OnDisable()
    {
        AllBushes.Remove(this);
    }

    void Start()
    {
        SetupGrassColliders();
    }

    public void SetupGrassColliders()
    {
        // Çimen katmanının (Layer 2) karakterleri (Layer 6) ve düşmanları (Layer 8) fiziksel olarak itmesini kapat
        Physics.IgnoreLayerCollision(GrassLayer, 0, true);
        Physics.IgnoreLayerCollision(GrassLayer, 6, true);
        Physics.IgnoreLayerCollision(GrassLayer, 8, true);

        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf == null || mf.sharedMesh == null) continue;
            // Ana zemin objesi (Object_4) çimen değildir, sadece Object_4.001 ve Object_5* çimendir
            if (mf.gameObject.name == "Object_4" || mf.gameObject.name == "Object_4.002") continue;

            mf.gameObject.layer = GrassLayer;
            var mc = mf.GetComponent<MeshCollider>();
            if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.enabled = true;
        }
        collidersConfigured = true;
    }

    public static bool IsPointInAnyBush(Vector3 worldPos)
    {
        if (!collidersConfigured)
        {
            for (int i = 0; i < AllBushes.Count; i++)
            {
                if (AllBushes[i] != null) AllBushes[i].SetupGrassColliders();
            }
            collidersConfigured = true;
        }

        int grassMask = 1 << GrassLayer;
        bool prevBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;

        bool hit = false;

        // 1. Karakterin gövde/ayak çevresinde 1.65m yarıçaplı küre içinde herhangi bir çimen yaprağı (ön veya arka yüz) var mı?
        if (Physics.CheckSphere(worldPos + Vector3.up * 0.6f, 1.65f, grassMask, QueryTriggerInteraction.Collide))
        {
            hit = true;
        }
        // 2. Yukarıdan aşağıya küre taraması (çimenlerin arasındaki boşlukları da kapsar)
        else if (Physics.SphereCast(worldPos + Vector3.up * 4.0f, 1.65f, Vector3.down, out _, 6.5f, grassMask, QueryTriggerInteraction.Collide))
        {
            hit = true;
        }

        Physics.queriesHitBackfaces = prevBackfaces;
        return hit;
    }
}
