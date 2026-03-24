using UnityEngine;

// ============================================================
// PlayerCameraFollow
// Attach this to the CAMERA GameObject (not the Manager).
// GameRenderer calls SetPlayerPosition() every frame.
// The camera smoothly follows the player with a fixed Z offset.
// ============================================================
[RequireComponent(typeof(Camera))]
public class PlayerCameraFollow : MonoBehaviour
{
    [Header("Follow Settings")]
    public Vector3 offset      = new Vector3(0f, 2f, -15f);
    public float   smoothSpeed = 0.12f;

    [Header("Axis Follow")]
    public bool followX = true;
    public bool followY = true;
    // Z is always fixed - this is what keeps the game 2-D.

    [Header("Camera Constraints (optional)")]
    public bool    useConstraints = false;
    public Vector2 xConstraint   = new Vector2(-200f, 200f);
    public Vector2 yConstraint   = new Vector2(-20f,  50f);

    [Header("Orthographic Size")]
    public float orthographicSize = 8f;

    // Set by GameRenderer every Update()
    private Vector3 _playerPosition;
    private bool    _hasTarget = false;

    // ----------------------------------------------------------
    void Start()
    {
        Camera cam = GetComponent<Camera>();
        cam.orthographic     = true;
        cam.orthographicSize = orthographicSize;

        // Start at the offset so there is no initial pop
        transform.position = new Vector3(
            offset.x, offset.y, offset.z);
    }

    // Called by GameRenderer.Update()
    public void SetPlayerPosition(Vector3 position)
    {
        _playerPosition = position;
        _hasTarget      = true;
    }

    // LateUpdate runs after all Update() calls, so the player
    // has already moved before we reposition the camera.
    void LateUpdate()
    {
        if (!_hasTarget) return;

        Vector3 desired = transform.position;

        if (followX) desired.x = _playerPosition.x + offset.x;
        if (followY) desired.y = _playerPosition.y + offset.y;
        desired.z = offset.z;  // always fixed

        if (useConstraints)
        {
            desired.x = Mathf.Clamp(desired.x, xConstraint.x, xConstraint.y);
            desired.y = Mathf.Clamp(desired.y, yConstraint.x, yConstraint.y);
        }

        transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed);
    }
}
