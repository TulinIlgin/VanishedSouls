using UnityEngine;

// Düşman. Küpe (sonra gerçek düşmana) ekle.
public class Enemy : MonoBehaviour
{
    bool dead;

    public void Die()
    {
        if (dead) return;
        dead = true;
        // TODO: ölüm animasyonu buraya → GetComponent<Animator>().SetTrigger("Die"); sonra Destroy(gameObject, süre);
        Destroy(gameObject);
    }
}
