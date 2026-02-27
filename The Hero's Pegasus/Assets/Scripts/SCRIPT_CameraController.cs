using UnityEngine;
using UnityEngine.InputSystem;

public class SCRIPT_CameraController : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Third-Person Distance")]
    public float distance    = 6f;
    public float minDistance = 0.5f;    // Below this → first-person
    public float maxDistance = 20f;
    public float scrollSpeed = 0.05f;

    [Header("Third-Person Offsets")]
    public float heightOffset  = 1.5f;
    public float lateralOffset = 0f;

    [Header("Follow Smoothing")]
    [Tooltip("Spring smooth time for position (seconds). Lower = snappier. ~0.15 is a good start.")]
    public float positionSmoothTime = 0.15f;
    [Tooltip("Exponential smooth speed for look direction. Higher = snappier.")]
    public float rotationSmoothing  = 10f;

    [Header("First-Person")]
    public Vector3 firstPersonLocalOffset = new Vector3(0f, 1.7f, 0.3f);

    // ── private state ──────────────────────────────────────────────────────────
    private bool    isFirstPerson;
    private Vector3 positionVelocity = Vector3.zero;    // SmoothDamp internal velocity
    private Vector3 smoothLookDir;                       // Smoothed world-space look direction
    private SCRIPT_PlayerMovementController playerMovement;
    private bool    holdCameraActive;
    private Vector3 heldForward;

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        // Seed look direction so there's no pop on the first frame
        if (target != null)
            smoothLookDir = target.forward;
        else
            smoothLookDir = transform.forward;
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (playerMovement == null)
            playerMovement = target.GetComponentInParent<SCRIPT_PlayerMovementController>();

        bool shouldHoldCamera = playerMovement != null && playerMovement.HoldCameraUntilBurst;
        if (shouldHoldCamera && !holdCameraActive)
        {
            holdCameraActive = true;
            heldForward = target.forward;
            if (heldForward.sqrMagnitude < 0.0001f) heldForward = transform.forward;
            heldForward.Normalize();
        }
        else if (!shouldHoldCamera)
        {
            holdCameraActive = false;
        }

        HandleScroll();

        isFirstPerson = (distance <= minDistance);

        if (isFirstPerson)
            ApplyFirstPerson();
        else
            ApplyThirdPerson();
    }

    // ──────────────────────────────────────────────────────────────────────────

    void HandleScroll()
    {
        float scroll = Mouse.current.scroll.ReadValue().y;
        distance -= scroll * scrollSpeed;
        distance  = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    void ApplyThirdPerson()
    {
        // The point in world space the camera orbits around and looks toward
        Vector3 pivot = target.position
                      + target.up    * heightOffset
                      + target.right * lateralOffset;

        Vector3 followForward = holdCameraActive ? heldForward : target.forward;
        Vector3 desiredPosition = pivot - followForward * distance;

        // SmoothDamp is a spring-damper: framerate-independent, no overshoot,
        // absorbs sudden direction changes gracefully.
        transform.position = Vector3.SmoothDamp(
            transform.position, desiredPosition,
            ref positionVelocity, positionSmoothTime);

        // Compute the direction from the (already smoothed) camera position to the pivot,
        // then ease the look direction with framerate-independent exponential smoothing.
        // Using 1-exp(-k*dt) instead of k*dt makes it consistent at any framerate.
        Vector3 desiredLookDir = (pivot - transform.position).normalized;
        float   t              = 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime);
        smoothLookDir = Vector3.Slerp(smoothLookDir, desiredLookDir, t);

        if (smoothLookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(smoothLookDir);
    }

    void ApplyFirstPerson()
    {
        // Reset velocity so there's no stale spring state if we return to third-person
        positionVelocity = Vector3.zero;
        smoothLookDir    = target.forward;

        transform.position = target.TransformPoint(firstPersonLocalOffset);
        transform.rotation = target.rotation;
    }
}
