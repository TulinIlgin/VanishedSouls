using UnityEngine;

/// <summary>
/// Model alt obje (Child) olduğunda, Aria'nın Animation Event'lerini ana objedeki AriaController'a iletir.
/// Animator bileşeni bir alt objedeyse bu scripti o alt objeye ekleyin.
/// </summary>
public class AriaAnimationEvents : MonoBehaviour
{
    AriaController aria;

    void Awake()
    {
        aria = GetComponentInParent<AriaController>();
    }

    /// <summary>
    /// Bıçak vuruş anı (darbenin indiği kare)
    /// </summary>
    public void OnKnifeHit()
    {
        if (aria == null) aria = GetComponentInParent<AriaController>();
        if (aria != null) aria.OnKnifeHit();
    }

    /// <summary>
    /// Bıçak animasyonu bitiş anı (animasyonun son karesi)
    /// </summary>
    public void FinishKnifeAttack()
    {
        if (aria == null) aria = GetComponentInParent<AriaController>();
        if (aria != null) aria.FinishKnifeAttack();
    }

    /// <summary>
    /// Taşın elden fırlatıldığı an (kolun taşı bıraktığı kare)
    /// </summary>
    public void OnThrowRelease()
    {
        if (aria == null) aria = GetComponentInParent<AriaController>();
        if (aria != null) aria.OnThrowRelease();
    }

    /// <summary>
    /// Taş atış animasyonu bitiş anı (animasyonun son karesi)
    /// </summary>
    public void FinishThrow()
    {
        if (aria == null) aria = GetComponentInParent<AriaController>();
        if (aria != null) aria.FinishThrow();
    }

    /// <summary>
    /// Ortak bitiş fonksiyonu (Her iki animasyonun sonuna da eklenebilir)
    /// </summary>
    public void FinishAttack()
    {
        if (aria == null) aria = GetComponentInParent<AriaController>();
        if (aria != null) aria.FinishAttack();
    }

    // Alternatif isimler
    public void OnAttackHit() => OnKnifeHit();
    public void ReleaseStone() => OnThrowRelease();
    public void ThrowStone() => OnThrowRelease();
}
