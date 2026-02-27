using UnityEngine;
using UnityEngine.InputSystem;

public class SCRIPT_CameraController : MonoBehaviour
{
    [Header("Target")]
    public Transform target;                        // Drag the player here in the Inspector

    [Header("Third-Person Distance")]
    public float distance    = 6f;
    public float minDistance = 0.5f;               // Below this → first-person
    public float maxDistance = 20f;
    public float scrollSpeed = 0.05f;              // Scroll is in high-res units in new Input System

    [Header("Third-Person Offsets")]
    public float heightOffset  = 1.5f;
    public float lateralOffset = 0f;

    [Header("Follow Smoothing")]
    public float positionSmoothing = 8f;
    public float rotationSmoothing = 8f;

    [Header("First-Person")]
    public Vector3 firstPersonLocalOffset = new Vector3(0f, 1.7f, 0.3f);

    // ── private state ──────────────────────────────────────────────────────────
    private bool isFirstPerson;

    // ──────────────────────────────────────────────────────────────────────────

    void LateUpdate()
    {
        if (target == null) return;

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
        // scroll.ReadValue().y is in pixels/notches — divide to normalise
        float scroll = Mouse.current.scroll.ReadValue().y;
        distance -= scroll * scrollSpeed;
        distance  = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    void ApplyThirdPerson()
    {
        Vector3 pivotWorld = target.position
                           + target.up    * heightOffset
                           + target.right * lateralOffset;

        Vector3 desiredPosition = pivotWorld - target.forward * distance;

        transform.position = Vector3.Lerp(transform.position, desiredPosition,
                                          Time.deltaTime * positionSmoothing);

        Quaternion desiredRotation = Quaternion.LookRotation(pivotWorld - transform.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation,
                                              Time.deltaTime * rotationSmoothing);
    }

    void ApplyFirstPerson()
    {
        transform.position = target.TransformPoint(firstPersonLocalOffset);
        transform.rotation = target.rotation;
    }
}
