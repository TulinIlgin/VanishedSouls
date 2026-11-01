using UnityEngine;

// Düşman. Küpe (sonra gerçek düşmana) ekle.
public class Enemy : MonoBehaviour
{
    protected bool dead;

    public virtual void Die()
    {
        if (dead) return;
        dead = true;
        // TODO: ölüm animasyonu buraya → GetComponent<Animator>().SetTrigger("Die"); sonra Destroy(gameObject, süre);
        Destroy(gameObject);
    }
}
