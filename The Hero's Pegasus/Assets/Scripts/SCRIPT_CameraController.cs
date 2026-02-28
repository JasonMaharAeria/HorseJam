using UnityEngine;
using UnityEngine.InputSystem;

public class SCRIPT_CameraController : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Third-Person Distance")]
    public float distance    = 6f;
    public float minDistance = 0.5f;
    public float maxDistance = 20f;
    public float scrollSpeed = 0.05f;

    [Header("Third-Person Offsets")]
    public float heightOffset  = 1.5f;
    public float lateralOffset = 0f;

    [Header("Follow Smoothing")]
    [Tooltip("Spring smooth time for position (seconds).")]
    public float positionSmoothTime = 0.15f;
    [Tooltip("Exponential smooth speed for look direction.")]
    public float rotationSmoothing  = 10f;

    [Header("First-Person")]
    public Vector3 firstPersonLocalOffset = new Vector3(0f, 1.7f, 0.3f);

    [Header("First-Person — Mesh Hiding")]
    [Tooltip("Renderers disabled in first-person. Leave empty to auto-find on the target's root.")]
    public Renderer[] meshesToHide;

    [Header("Vertical Loop — Cinematic Camera")]
    [Tooltip("Camera distance during the loop. Can freely exceed maxDistance.")]
    public float loopZoomDistance = 35f;
    [Tooltip("Maximum horizontal swing of the boom arm in degrees. " +
             "90 = side-on view at the apex, 60 = partial swing.")]
    public float loopBoomAngle = 80f;
    [Tooltip("Smooth time for the boom-arm position during the loop.")]
    public float loopPositionSmoothTime = 0.25f;

    // ── private state ──────────────────────────────────────────────────────────
    private bool    _frozen;
    private bool    isFirstPerson;
    private bool    _wasFirstPerson;
    private Vector3 positionVelocity;
    private Vector3 smoothLookDir;
    private SCRIPT_PlayerMovementController playerMovement;

    // Circle-flip camera hold
    private bool    holdCameraActive;
    private Vector3 heldForward;

    // Loop cinematic state
    private bool  _wasLooping;
    private float _loopStartDistance;  // captured at loop start so zoom interpolates from here

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        smoothLookDir = target != null ? target.forward : transform.forward;
    }

    /// <summary>Lock the camera in place. Called by SCRIPT_PlayerMovementController on death.</summary>
    public void Freeze() => _frozen = true;

    void LateUpdate()
    {
        if (target == null || _frozen) return;

        if (playerMovement == null)
            playerMovement = target.GetComponentInParent<SCRIPT_PlayerMovementController>();

        // ── detect loop start to snapshot current zoom distance ────────────────
        bool isLooping = playerMovement != null && playerMovement.IsLooping;
        if (isLooping && !_wasLooping)
            _loopStartDistance = distance;
        _wasLooping = isLooping;

        // ── circle-flip hold-camera logic (loop bypasses this entirely) ────────
        if (!isLooping)
        {
            bool shouldHold = playerMovement != null && playerMovement.HoldCameraUntilBurst;
            if (shouldHold && !holdCameraActive)
            {
                holdCameraActive = true;
                heldForward      = target.forward;
                if (heldForward.sqrMagnitude < 0.0001f) heldForward = transform.forward;
                heldForward.Normalize();
            }
            else if (!shouldHold)
            {
                holdCameraActive = false;
            }
        }

        HandleScroll();

        if (isLooping)
        {
            ApplyLoopCamera();
        }
        else
        {
            isFirstPerson = (distance <= minDistance);

            if (isFirstPerson != _wasFirstPerson)
            {
                SetMeshVisibility(!isFirstPerson);
                _wasFirstPerson = isFirstPerson;
            }

            if (isFirstPerson)
                ApplyFirstPerson();
            else
                ApplyThirdPerson();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────

    void SetMeshVisibility(bool visible)
    {
        if ((meshesToHide == null || meshesToHide.Length == 0) && target != null)
            meshesToHide = target.root.GetComponentsInChildren<Renderer>();

        foreach (Renderer r in meshesToHide)
            if (r != null) r.enabled = visible;
    }

    void HandleScroll()
    {
        float scroll = Mouse.current.scroll.ReadValue().y;
        distance -= scroll * scrollSpeed;
        distance  = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    void ApplyThirdPerson()
    {
        Vector3 pivot = target.position
                      + target.up    * heightOffset
                      + target.right * lateralOffset;

        Vector3 followForward  = holdCameraActive ? heldForward : target.forward;
        Vector3 desiredPosition = pivot - followForward * distance;

        transform.position = Vector3.SmoothDamp(
            transform.position, desiredPosition,
            ref positionVelocity, positionSmoothTime);

        Vector3 desiredLookDir = (pivot - transform.position).normalized;
        float   t              = 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime);
        smoothLookDir = Vector3.Slerp(smoothLookDir, desiredLookDir, t);

        if (smoothLookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(smoothLookDir);
    }

    /// <summary>
    /// Cinematic boom-arm camera for the vertical loop maneuver.
    ///
    /// The boom starts directly behind the pegasus, swings horizontally to a
    /// side-on position at the apex (giving a profile view of the loop), then
    /// swings back behind as the loop completes. Distance also zooms out and back.
    ///
    /// Swing curve:  sin(progress × π)  — 0 at start, peaks at apex, 0 at end.
    /// The boom pivots around world-up so the swing is always horizontal regardless
    /// of the player's pitch (the whole point of the maneuver is the vertical arc).
    /// </summary>
    void ApplyLoopCamera()
    {
        float progress = playerMovement.LoopProgress;

        // Sine curve: 0 → 1 → 0 over the full loop (peak at apex, t = 0.5)
        float swing = Mathf.Sin(progress * Mathf.PI);

        // Boom angle: 0° behind → loopBoomAngle° to the side → 0° behind
        float boomAngle = loopBoomAngle * swing;

        // Zoom: interpolate from the distance at loop start to loopZoomDistance
        float targetDist = Mathf.Lerp(_loopStartDistance, loopZoomDistance, swing);

        // Boom arm direction: start behind the entry forward, rotate around world-up
        Vector3 behind  = -playerMovement.LoopEntryForward;
        Vector3 boomDir = Quaternion.AngleAxis(boomAngle, Vector3.up) * behind;
        boomDir.Normalize();

        Vector3 pivot       = target.position + Vector3.up * heightOffset;
        Vector3 desiredPos  = pivot + boomDir * targetDist;

        transform.position = Vector3.SmoothDamp(
            transform.position, desiredPos,
            ref positionVelocity, loopPositionSmoothTime);

        // Always look at the pivot so the pegasus stays in frame throughout.
        Vector3 lookDir = (pivot - transform.position).normalized;
        float   t       = 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime);
        smoothLookDir   = Vector3.Slerp(smoothLookDir, lookDir, t);

        if (smoothLookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(smoothLookDir);
    }

    void ApplyFirstPerson()
    {
        positionVelocity = Vector3.zero;
        smoothLookDir    = target.forward;

        transform.position = target.TransformPoint(firstPersonLocalOffset);
        transform.rotation = target.rotation;
    }
}
