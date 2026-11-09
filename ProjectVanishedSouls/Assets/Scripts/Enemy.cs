using UnityEngine;

// Düşman temel sınıfı
public class Enemy : MonoBehaviour
{
    protected bool dead;

    public virtual void Die()
    {
        if (dead) return;
        dead = true;
        Destroy(gameObject);
    }
}
