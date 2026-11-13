using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lumen'in özel pasif keşif sistemi.
/// Tuşa basmaya gerek kalmadan:
/// 1) Sisi sadece kendi etrafında belirlediğin kadar (Fog Vision Radius) dar bir alanda yarar.
/// 2) Sisi kaldırmadığı halde, çok daha geniş bir menzildeki (Detection Radius) düşmanları
///    sisin ardında kırmızı çizgili siluet (Red Silhouette) olarak otomatik gösterir.
/// </summary>
public class LumenScoutSkill : MonoBehaviour
{
    [Header("1. Sisi Ne Kadar Yaracak?")]
    [Tooltip("Lumen'in fiziksel olarak sisi açacağı dar alanın yarıçapı (metre)")]
    [SerializeField] private float fogVisionRadius = 6f;

    [Header("2. Düşmanları Ne Kadar Uzaktan Kırmızı Görecek?")]
    [Tooltip("Sisi açmadan düşmanları kırmızı çizgili siluet olarak algılama menzili (metre)")]
    [SerializeField] private float detectionRadius = 25f;

    [Tooltip("Tarama sıklığı (saniye cinsinden, 0.04 = akıcı ve performans dostu)")]
    [SerializeField] private float scanInterval = 0.04f;

    private float timer = 0f;
    private readonly HashSet<FogVisibility> currentlyDetected = new HashSet<FogVisibility>();

    public float FogVisionRadius
    {
        get { return fogVisionRadius; }
        set { fogVisionRadius = Mathf.Max(0.5f, value); SyncWithFogOfWar(); }
    }

    public float DetectionRadius
    {
        get { return detectionRadius; }
        set { detectionRadius = Mathf.Max(1f, value); }
    }

    private void Start()
    {
        SyncWithFogOfWar();
    }

    private void OnValidate()
    {
        SyncWithFogOfWar();
    }

    /// <summary>
    /// FogOfWar sistemindeki Lumen üyesinin sis açma yarıçapını doğrudan günceller.
    /// </summary>
    void SyncWithFogOfWar()
    {
        if (FogOfWar.Instance == null) return;

        var members = FogOfWar.Instance.TeamMembers;
        if (members == null) return;

        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            if (m != null && m.character == transform)
            {
                m.visionRadius = fogVisionRadius;
                break;
            }
        }
    }

    private void Update()
    {
        if (GameMenuManager.IsMenuOpen) return;

        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            timer = scanInterval;
            PerformDetectionScan();
        }
    }

    void PerformDetectionScan()
    {
        Vector3 myPos = transform.position;
        float radSqr = detectionRadius * detectionRadius;

        var allEnemies = FindObjectsByType<FogVisibility>(FindObjectsSortMode.None);
        var newlyDetected = new HashSet<FogVisibility>();

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

        // Daha önce menzilde olup şimdi çıkan düşmanların işaretini kaldır
        foreach (var old in currentlyDetected)
        {
            if (old != null && !newlyDetected.Contains(old))
            {
                old.SetDetectedByLumen(false);
            }
        }

        currentlyDetected.Clear();
        foreach (var n in newlyDetected)
        {
            currentlyDetected.Add(n);
        }
    }

    private void OnDisable()
    {
        foreach (var enemy in currentlyDetected)
        {
            if (enemy != null) enemy.SetDetectedByLumen(false);
        }
        currentlyDetected.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        // 1. Sisi yarma menzili (Sarı Çember)
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, fogVisionRadius);

        // 2. Kırmızı çizgili düşman algılama menzili (Kırmızı Çember)
        Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}
