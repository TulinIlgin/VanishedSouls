using System.Collections.Generic;
using UnityEngine;

// Çimen / çalı gizlenme alanı.
// KURAL:
// 1. Düşman arkasını döndüğünde (bizi görmüyorken) veya bizi fark etmeden (0.1 sn hata payı içinde) çalıya girersek,
//    çalıdayken düşman dibimizden geçse bile bizi ASLA göremez.
// 2. Ancak düşman bizi dışarıda 0.1 saniyeden uzun süre gördükten sonra (kırmızı alana/kovalamaya geçtikten sonra)
//    onun gözü önünde çalıya girersek, çalı bizi koruyamaz; düşman çalının içine gelip bizi yakalar.
public class Bush : MonoBehaviour
{
    public static readonly List<Bush> AllBushes = new List<Bush>();

    // Düşmanın hangi karakteri çalıya girmeden önce (0.1sn hata payı sonrasında) gördüğünü tutar
    static readonly HashSet<(EnemyAI enemy, PlayerCharacter player)> spottedBeforeBushPairs = new HashSet<(EnemyAI, PlayerCharacter)>();

    // Ignore Raycast katmanı (Layer 2): Tıklamaları ve NavMesh'i engellemez ama fizik taraması ile gerçek çimen üçgenlerini yakalar
    const int GrassLayer = 2;

    [Tooltip("Çimenlerin arasındaki boşlukları da kapsama yarıçapı (metre)")]
    public float stealthRadius = 1.65f;

    [Tooltip("Düşmanın bizi gördükten sonra öldürme/kovalama moduna geçmesi için hata payı süresi (saniye)")]
    public const float DetectionGraceTime = 0.1f;

    static bool collidersConfigured;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        AllBushes.Clear();
        spottedBeforeBushPairs.Clear();
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

    // Düşman karakteri 0.1 saniye boyunca açıkta gördüğünde çağrılır:
    // Artık bu karakter bu düşmanın gözü önünde çalıya girse bile çalı etki etmez!
    public static void MarkSpottedBeforeBush(EnemyAI enemy, PlayerCharacter player)
    {
        if (enemy == null || player == null) return;
        spottedBeforeBushPairs.Add((enemy, player));
    }

    // Düşman takibi bıraktığında veya karakter düşmanın görüşünden tamamen kurtulduğunda sıfırlanır
    public static void ClearSpottedState(EnemyAI enemy, PlayerCharacter player = null)
    {
        if (enemy == null) return;
        if (player != null)
        {
            spottedBeforeBushPairs.Remove((enemy, player));
            return;
        }

        spottedBeforeBushPairs.RemoveWhere(pair => pair.enemy == enemy);
    }

    public static bool WasSpottedBeforeEnteringBush(EnemyAI enemy, PlayerCharacter player)
    {
        if (enemy == null || player == null) return false;
        return spottedBeforeBushPairs.Contains((enemy, player));
    }

    // Karakterin belirli bir düşmana karşı çalıda gerçekten gizli olup olmadığını döndürür.
    // Düşman karakteri dışarıda 0.1sn+ gördükten sonra karakter çalıya girdiyse false döner (düşman gelip yakalar).
    // Düşman görmeden (veya 0.1sn hata payı içinde) çalıya girdiyse true döner (dibinden geçse bile göremez).
    public static bool IsHiddenFromEnemy(PlayerCharacter player, EnemyAI enemy)
    {
        if (player == null) return false;
        bool physicallyInBush = player.isHidden || IsPointInAnyBush(player.WorldPosition);
        if (!physicallyInBush) return false;

        if (enemy != null && WasSpottedBeforeEnteringBush(enemy, player))
            return false;

        return true;
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
