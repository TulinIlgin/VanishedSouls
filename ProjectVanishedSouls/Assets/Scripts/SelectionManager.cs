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
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (selectedAria == null) selectedAria = FindFirstObjectByType<AriaController>();
        if (selectedAria == null) return;

        // fare yürüme
        if (mouse.leftButton.isPressed)
        {
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, groundLayer))
                selectedAria.MoveTo(hit.point);
        }
        // Sol tık bırakıldı → dur
        if (mouse.leftButton.wasReleasedThisFrame)
            selectedAria.Stop();

        // Sağ tık → düşmana taş at
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
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        Vector3 myPos = selectedAria.transform.position;

        // Düşmanın üstüne tıkladıysa ona at
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, enemyLayer))
        {
            if (Vector3.Distance(myPos, hit.transform.position) <= selectedAria.throwRange)
                selectedAria.ThrowStoneAt(hit.transform);
            else
                Debug.Log("Hedef menzil dışında!");
            return;
        }

        // Boşa tıkladıysa menzildeki en yakın düşmana at
        Transform best = null;
        float bestDist = float.MaxValue;
        foreach (var c in Physics.OverlapSphere(myPos, selectedAria.throwRange, enemyLayer))
        {
            float d = Vector3.Distance(myPos, c.transform.position);
            if (d < bestDist) { bestDist = d; best = c.transform; }
        }
        if (best != null) selectedAria.ThrowStoneAt(best);
    }

    void Deselect()
    {
        if (selectedRenderer != null && defaultMaterial != null)
            selectedRenderer.material = defaultMaterial;

        selectedAria     = null;
        selectedRenderer = null;
    }
}
