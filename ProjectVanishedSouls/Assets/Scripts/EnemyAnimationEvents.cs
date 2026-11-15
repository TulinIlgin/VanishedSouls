using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Model alt obje (Child) olduğunda, Animation Event'lerini ana objedeki EnemyAI'a iletir.
/// </summary>
public class EnemyAnimationEvents : MonoBehaviour
{
    EnemyAI enemyAI;
    public List<AudioClip> Sounds;
    public AudioSource audioSource;

    void Awake()
    {
        enemyAI = GetComponentInParent<EnemyAI>();
    }

    /// <summary>
    /// Vuruş anı: Animasyonda okun fırlatıldığı / darbenin indiği kareye koyun.
    /// </summary>
    public void OnAttackHit()
    {
        if (enemyAI == null) enemyAI = GetComponentInParent<EnemyAI>();
        if (enemyAI != null)
        {
            audioSource.PlayOneShot(Sounds[1]);
            enemyAI.OnAttackHit();
        }
    }

    public void Detected()
    {
        audioSource.PlayOneShot(Sounds[0]);
    }

    /// <summary>
    /// Bitiş anı: Animasyonun EN SON karesine koyun. Bu çağrılana kadar karakter hareket edemez.
    /// </summary>
    public void FinishAttack()
    {
        if (enemyAI == null) enemyAI = GetComponentInParent<EnemyAI>();
        if (enemyAI != null)
        {
            enemyAI.FinishAttack();
        }
    }
}
