using UnityEngine;

/// <summary>
/// Model alt obje (Child) olduğunda, Animation Event'lerini ana objedeki EnemyAI'a iletir.
/// </summary>
public class EnemyAnimationEvents : MonoBehaviour
{
    EnemyAI enemyAI;

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
            enemyAI.OnAttackHit();
        }
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
