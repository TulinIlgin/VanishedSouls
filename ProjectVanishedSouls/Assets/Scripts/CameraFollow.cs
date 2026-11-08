using UnityEngine;

// kamera izometrik ariayı takip eder
public class CameraFollow : MonoBehaviour
{
    [Header("Takip Ayarları")]
    [Tooltip("Takip edilecek karakter (Boş bırakılırsa Aria otomatik bulunur)")]
    public Transform target;

    [Tooltip("Takip yumuşaklığı (düşük değer = daha hızlı, yüksek değer = daha yumuşak takip)")]
    public float smoothTime = 0.18f;

    [Tooltip("Karakterin gövde hizası için odak yüksekliği")]
    public float targetHeightOffset = 1.0f;

    float initialCameraY;
    Quaternion initialRotation;
    Vector3 currentVelocity;
    bool initialized;

    void Awake()
    {
        InitializeCamera();
    }

    void Start()
    {
        InitializeCamera();
        SnapToTarget();
    }

    void InitializeCamera()
    {
        if (initialized) return;
        initialCameraY = transform.position.y > 5f ? transform.position.y : 20f;
        initialRotation = transform.rotation;
        FindTargetIfNeeded();
        initialized = true;
    }

    void FindTargetIfNeeded()
    {
        if (target != null) return;
        var aria = FindAnyObjectByType<AriaController>();
        if (aria != null)
            target = aria.transform;
    }
    public void SnapToTarget()
    {
        FindTargetIfNeeded();
        if (target == null) return;

        transform.rotation = initialRotation;
        transform.position = CalculateDesiredPosition();
        currentVelocity = Vector3.zero;
    }

    Vector3 CalculateDesiredPosition()
    {
        Vector3 focusPoint = target.position + Vector3.up * targetHeightOffset;
        Vector3 fwd = transform.forward;
        float downComponent = Mathf.Max(0.15f, -fwd.y);
        float distAlongViewRay = (initialCameraY - focusPoint.y) / downComponent;
        return focusPoint - fwd * distAlongRay(distAlongViewRay);
    }

    float distAlongRay(float d) => d;

    void LateUpdate()
    {
        FindTargetIfNeeded();
        if (target == null) return;

        transform.rotation = initialRotation;
        Vector3 desiredPos = CalculateDesiredPosition();
        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref currentVelocity, smoothTime);
    }
}
