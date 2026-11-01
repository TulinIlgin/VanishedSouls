using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Oyundaki tüm dost karakterler (Aria, Lumen vb.) için ortak temel sınıf.
// Düşmanlar sadece Aria'ya değil, bu sınıfa sahip tüm karakterlere saldırır.
public class PlayerCharacter : MonoBehaviour
{
    public static readonly List<PlayerCharacter> AllPlayers = new List<PlayerCharacter>();

    [Header("Karakter Bilgisi")]
    public string characterName = "Aria";

    [Header("Gizlilik Durumu")]
    [Tooltip("Karakter şu an çimende gizleniyor mu?")]
    public bool isHidden = false;

    [Header("Can Durumu")]
    public int maxHealth = 3;
    public int currentHealth = 3;
    public bool diePermanentlyOnHit = false;

    public bool IsDead { get; private set; }

    Renderer cachedRenderer;
    LumenController lumenCtrl;
    Vector3 startPosition;
    bool startPosRecorded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        AllPlayers.Clear();
    }

    // Karakterin dünyadaki gerçek taban pozisyonu (FBX pivot kaymalarını önler)
    public Vector3 WorldPosition
    {
        get
        {
            if (GetComponent<AriaController>() != null || GetComponent<LumenController>() != null)
                return transform.position;

            if (cachedRenderer == null)
            {
                foreach (var r in GetComponentsInChildren<Renderer>())
                {
                    if (r != null && r.enabled && !r.name.ToLower().Contains("knife"))
                    {
                        cachedRenderer = r;
                        break;
                    }
                }
            }
            if (cachedRenderer != null)
            {
                Bounds b = cachedRenderer.bounds;
                return new Vector3(b.center.x, b.min.y, b.center.z);
            }
            return transform.position;
        }
    }

    void OnEnable()
    {
        IsDead = false;
        lumenCtrl = GetComponent<LumenController>();
        if (!AllPlayers.Contains(this))
            AllPlayers.Add(this);
        currentHealth = maxHealth;
        if (!startPosRecorded)
        {
            startPosition = transform.position;
            startPosRecorded = true;
        }
    }

    void OnDisable()
    {
        AllPlayers.Remove(this);
    }

    void Update()
    {
        if (IsDead) return;

        if (!AllPlayers.Contains(this))
            AllPlayers.Add(this);

        if (lumenCtrl == null)
            lumenCtrl = GetComponent<LumenController>();

        bool inBush = Bush.IsPointInAnyBush(WorldPosition) || (lumenCtrl != null && lumenCtrl.IsHiddenWithAria());
        if (inBush != isHidden)
        {
            SetHidden(inBush);
        }
    }

    public void SetHidden(bool hidden)
    {
        if (isHidden == hidden) return;
        isHidden = hidden;
        Debug.Log($"{characterName} {(hidden ? "çimene girdi (GÖRÜNMEZ)" : "çimenden çıktı (GÖRÜNÜR)")}");
    }

    public virtual void TakeHit()
    {
        if (IsDead) return;

        // Lumen gibi tek vuruşta ölen karakterler devrilip yok olsun
        if (diePermanentlyOnHit || characterName == "Lumen")
        {
            Debug.Log($"{characterName} düşman tarafından vuruldu ve öldü!");
            StartCoroutine(DieFallOverRoutine());
            return;
        }

        currentHealth = Mathf.Max(0, currentHealth - 1);
        Debug.Log($"{characterName} düşmandan saldırı aldı! Kalan Can: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0)
        {
            Debug.Log($"{characterName} yakalandı! Başlangıç noktasına dönüyor...");
            currentHealth = maxHealth;
            var agent = GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled)
            {
                agent.ResetPath();
                agent.Warp(startPosition);
            }
            else
            {
                transform.position = startPosition;
            }
        }
    }

    IEnumerator DieFallOverRoutine()
    {
        IsDead = true;
        AllPlayers.Remove(this);

        float duration = 0.35f;
        float rotated = 0f;

        if (lumenCtrl != null && lumenCtrl.visualModel != null)
        {
            // Lumen görsel modelini ayaklarının etrafında 90 derece yere yatır
            Vector3 footPivot = transform.position;
            while (rotated < 90f)
            {
                float step = (90f / duration) * Time.deltaTime;
                if (rotated + step > 90f) step = 90f - rotated;
                lumenCtrl.visualModel.RotateAround(footPivot, Vector3.right, step);
                rotated += step;
                yield return null;
            }
            yield return new WaitForSeconds(0.45f);
            lumenCtrl.HideVisualOnDeath();
            gameObject.SetActive(false);
            yield break;
        }

        Vector3 pivot = WorldPosition;
        Transform visualTarget = cachedRenderer != null ? cachedRenderer.transform : transform;
        while (rotated < 90f)
        {
            float step = (90f / duration) * Time.deltaTime;
            if (rotated + step > 90f) step = 90f - rotated;
            visualTarget.RotateAround(pivot, Vector3.right, step);
            rotated += step;
            yield return null;
        }

        yield return new WaitForSeconds(0.45f);
        gameObject.SetActive(false);
    }
}
