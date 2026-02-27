using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class SCRIPT_PlayerMovementController : MonoBehaviour
{
    [Header("Flight Speed")]
    public float flightSpeed  = 20f;
    public float acceleration = 10f;    // How snappily we reach flight speed

    [Header("Steering")]
    public float yawSensitivity   = 0.1f;  // Mouse X → left/right
    public float pitchSensitivity = 0.1f;  // Mouse Y → up/down
    public float maxPitchAngle    = 80f;   // Degrees, prevents full flip

    [Header("Banking (visual roll)")]
    public bool  enableBanking  = true;
    public float bankStrength   = 20f;    // Max roll degrees when turning
    public float bankSmoothing  = 5f;

    // ── private state ──────────────────────────────────────────────────────────
    private Rigidbody rb;
    private float currentYaw;
    private float currentPitch;
    private float currentBank;

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity      = false;
        rb.linearDamping   = 0.5f;
        rb.angularDamping  = 999f; // We drive rotation ourselves — kill physics spin

        // Seed yaw/pitch from the object's starting rotation
        currentYaw   = transform.eulerAngles.y;
        currentPitch = transform.eulerAngles.x;
        if (currentPitch > 180f) currentPitch -= 360f;
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    void Update()
    {
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        float mouseX =  mouseDelta.x;
        float mouseY =  mouseDelta.y;

        currentYaw   += mouseX * yawSensitivity;
        currentPitch -= mouseY * pitchSensitivity; // pulling back = pitch up
        currentPitch  = Mathf.Clamp(currentPitch, -maxPitchAngle, maxPitchAngle);

        if (enableBanking)
        {
            float targetBank = -mouseX * bankStrength;
            currentBank = Mathf.Lerp(currentBank, targetBank, Time.deltaTime * bankSmoothing);
        }
        else
        {
            currentBank = 0f;
        }

        transform.rotation = Quaternion.Euler(currentPitch, currentYaw, currentBank);
    }

    void FixedUpdate()
    {
        Vector3 targetVelocity = transform.forward * flightSpeed;
        rb.linearVelocity = Vector3.MoveTowards(rb.linearVelocity, targetVelocity,
                                                acceleration * Time.fixedDeltaTime);
    }
}
