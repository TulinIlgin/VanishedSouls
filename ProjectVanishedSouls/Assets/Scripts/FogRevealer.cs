using UnityEngine;

/// <summary>
/// Haritadaki lambalara, ampullere veya ek görüş sağlayacak objelere eklenen bileşen.
/// FogOfWar sistemine otomatik olarak kaydolur ve etrafındaki sisi dairesel olarak açar.
/// </summary>
public class FogRevealer : MonoBehaviour
{
    [Header("Görüş Yarıçapı ve Ofset")]
    [Tooltip("Bu ışık kaynağının sis üzerinde açacağı dairesel alanın yarıçapı")]
    [SerializeField] private float radius = 8f;
    [Tooltip("Işık merkezini objeye göre kaydırma ofseti")]
    [SerializeField] private Vector3 centerOffset = Vector3.zero;

    public float Radius
    {
        get { return radius; }
        set { radius = Mathf.Max(0f, value); }
    }

    public Vector3 CenterPosition
    {
        get { return transform.position + centerOffset; }
    }

    private void OnEnable()
    {
        FogOfWar.RegisterRevealer(this);
    }

    private void OnDisable()
    {
        FogOfWar.UnregisterRevealer(this);
    }

    private void OnDrawGizmosSelected()
    {
        // Editörde objeyi seçtiğinde görüş çemberini sarı renkte gösterir
        Gizmos.color = new Color(1f, 0.92f, 0.016f, 0.6f);
        Gizmos.DrawWireSphere(CenterPosition, radius);
    }
}
