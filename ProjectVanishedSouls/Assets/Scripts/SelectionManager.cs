using UnityEngine;
using UnityEngine.InputSystem;

public class SelectionManager : MonoBehaviour
{
    [Header("Seçim")]
    public LayerMask characterLayer;
    public LayerMask groundLayer;
    public LayerMask enemyLayer;

    [Header("Highlight")]
    public Material selectedMaterial;
    public Material defaultMaterial;

    AriaController selectedAria;
    Renderer selectedRenderer;

    Camera cam;

    void Awake() => cam = Camera.main;

    void Update()
    {
        if (GameMenuManager.IsMenuOpen) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        if (selectedAria == null) selectedAria = FindAnyObjectByType<AriaController>();
        if (selectedAria == null) return;

        // fare yürüme
        if (mouse.leftButton.isPressed)
        {
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, 500f, groundLayer))
                selectedAria.MoveTo(hit.point);
        }
        // Sol tık bırakıldı
        if (mouse.leftButton.wasReleasedThisFrame)
            selectedAria.Stop();

        // Sağ tık düşmana taş at
        if (mouse.rightButton.wasPressedThisFrame)
            TryAction();
    }

    void TrySelect()
    {
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, characterLayer))
        {
            Deselect();
            selectedAria     = hit.collider.GetComponent<AriaController>();
            selectedRenderer = hit.collider.GetComponentInChildren<Renderer>();

            if (selectedRenderer != null && selectedMaterial != null)
                selectedRenderer.material = selectedMaterial;
        }
        else
        {
            Deselect();
        }
    }

    void TryAction()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        Vector3 myPos = selectedAria.transform.position;

        if (selectedAria.HasStone)
        {
           
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, myPos.y, 0f));
            if (groundPlane.Raycast(ray, out float enterDist))
            {
                selectedAria.ThrowStoneToPoint(ray.GetPoint(enterDist));
                return;
            }

            if (Physics.Raycast(ray, out RaycastHit anyHit, 500f))
            {
                selectedAria.ThrowStoneToPoint(anyHit.point);
                return;
            }
        }

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, enemyLayer))
        {
            if (selectedAria.KnifeMode)
            {
                selectedAria.KnifeAttackAt(hit.transform);
                return;
            }

            Debug.Log("Düşmana saldırmak için önce F ile bıçağını çek veya E ile taş alıp sağ tıkla bir yere fırlat!");
            return;
        }

        if (selectedAria.KnifeMode)
        {
            Transform best = null;
            float bestDist = float.MaxValue;
            float searchRange = selectedAria.knifeRange + 3f;
            foreach (var c in Physics.OverlapSphere(myPos, searchRange, enemyLayer))
            {
                float d = Vector3.Distance(myPos, c.transform.position);
                if (d < bestDist) { bestDist = d; best = c.transform; }
            }

            if (best != null)
                selectedAria.KnifeAttackAt(best);
        }
    }

    void Deselect()
    {
        if (selectedRenderer != null && defaultMaterial != null)
            selectedRenderer.material = defaultMaterial;

        selectedAria     = null;
        selectedRenderer = null;
    }
}
