using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================
// PlayerController
// Handles all player movement, jumping, gravity, and
// collision response (damage, pickups, goal, instakill).
// Uses the new Unity Input System package.
//
// Jump-feel improvements:
//   • Coyote time  – player can still jump for a brief window
//                    after walking off a ledge
//   • Jump buffer  – a jump pressed just before landing still
//                    registers when the player touches the ground
// ============================================================
public class PlayerController : MonoBehaviour
{
    // ----------------------------------------------------------
    // Inspector settings
    // ----------------------------------------------------------
    [Header("Horizontal Movement")]
    [Tooltip("Top horizontal speed.")]
    public float maxSpeed           = 10f;
    [Tooltip("How quickly the player reaches max speed on the ground.")]
    public float groundAcceleration = 90f;
    [Tooltip("Friction applied on the ground when no input is held.")]
    public float groundDeceleration = 75f;
    [Tooltip("Acceleration while airborne (less control than ground).")]
    public float airAcceleration    = 45f;
    [Tooltip("Air friction when no input is held (low = floaty, high = snappy).")]
    public float airDeceleration    = 20f;

    [Header("Jump")]
    public float jumpForce          = 20f;
    [Tooltip("Tap jump: velocity is multiplied by this on early button release.")]
    [Range(0.1f, 0.9f)]
    public float jumpCutMultiplier  = 0.45f;

    [Header("Gravity")]
    public float riseGravity        = 35f;
    public float fallGravity        = 70f;
    [Tooltip("Near the apex of the jump, gravity is scaled down for hang time.")]
    public float hangGravityMult    = 0.35f;
    [Tooltip("|velocityY| below this value is considered the apex.")]
    public float hangSpeedThreshold = 3.5f;

    [Header("Jump Feel (Coyote Time & Buffer)")]
    [Tooltip("How long after walking off a ledge the player can still jump.")]
    public float coyoteTime     = 0.15f;
    [Tooltip("How early before landing a jump press is remembered.")]
    public float jumpBufferTime = 0.12f;

    [Header("Player Collider Size")]
    public float playerWidth  = 1f;
    public float playerHeight = 1f;
    public float playerDepth  = 0.5f;

    [Header("Spawn")]
    public Vector3 spawnPosition = new Vector3(0f, 0f, 0f);

    // ----------------------------------------------------------
    // Public read-only state
    // ----------------------------------------------------------
    public Vector3 Position     { get; private set; }
    public int     ColliderID   { get; private set; } = -1;
    public bool    IsGrounded   { get; private set; }
    public Vector3 ColliderSize => new Vector3(playerWidth, playerHeight, playerDepth);
    public Vector3 VisualScale  { get; private set; } = Vector3.one;

    // ----------------------------------------------------------
    // Private state
    // ----------------------------------------------------------
    private Vector3     _velocity;
    private GameManager _gameManager;
    private Keyboard    _kb;

    private float _coyoteTimeCounter;
    private float _jumpBufferCounter;

    // ── Squash & stretch animation ───────────────────────────
    private const float JumpSquashDuration = 0.12f;
    private float   _squashTimer;   // > 0 while takeoff squash is playing
    private float   _landTimer;     // > 0 while landing squash is playing
    private bool    _wasGrounded;   // grounded state from previous frame
    private float   _bobPhase;      // sin phase for walk bob

    // ----------------------------------------------------------
    // Unity lifecycle
    // ----------------------------------------------------------
    void Awake()
    {
        _gameManager = GetComponent<GameManager>();
    }

    void Start()
    {
        _kb = Keyboard.current;

        Position = spawnPosition;

        ColliderID = CollisionManager.Instance.RegisterCollider(
            Position, ColliderSize, true, EntityType.Player);

        SyncCollider();
    }

    void Update()
    {
        _kb = Keyboard.current;

        if (_kb == null)                                    return;
        if (_gameManager.CurrentState != GameState.Playing) return;

        // ── Coyote-time counter ──────────────────────────────
        // While grounded, keep the counter full.
        // The moment we leave the ground it counts down.
        if (IsGrounded)
            _coyoteTimeCounter = coyoteTime;
        else
            _coyoteTimeCounter -= Time.deltaTime;

        HandleInput();
        ApplyGravity();
        MoveHorizontal();
        MoveVertical();
        SyncCollider();
        UpdateVisualScale();

        _wasGrounded = IsGrounded;
        Position = new Vector3(Position.x, Position.y, 0f);
    }

    // ----------------------------------------------------------
    // Input
    // ----------------------------------------------------------
    void HandleInput()
    {
        bool jumpDown     = _kb.spaceKey.wasPressedThisFrame
                         || _kb.wKey.wasPressedThisFrame
                         || _kb.upArrowKey.wasPressedThisFrame;
        bool jumpReleased = _kb.spaceKey.wasReleasedThisFrame
                         || _kb.wKey.wasReleasedThisFrame
                         || _kb.upArrowKey.wasReleasedThisFrame;

        if (jumpDown)
            _jumpBufferCounter = jumpBufferTime;
        else
            _jumpBufferCounter -= Time.deltaTime;

        // ── Execute jump ────────────────────────────────────
        bool canJump = _jumpBufferCounter > 0f &&
                       (IsGrounded || _coyoteTimeCounter > 0f);
        if (canJump)
        {
            _velocity.y        = jumpForce;
            IsGrounded         = false;
            _coyoteTimeCounter = 0f;
            _jumpBufferCounter = 0f;
            _squashTimer       = JumpSquashDuration;  // trigger takeoff squash
        }

        // ── Variable jump height: cut velocity on early release ─
        // If the player releases jump while still rising, multiply
        // the upward velocity down → short hop. Holding gives full arc.
        if (jumpReleased && _velocity.y > 0f)
            _velocity.y *= jumpCutMultiplier;

        if (_kb.fKey.wasPressedThisFrame)
            _gameManager.UseFireball();
    }

    // ----------------------------------------------------------
    // Gravity
    // ----------------------------------------------------------
    void ApplyGravity()
    {
        if (IsGrounded)
        {
            _velocity.y = 0f;
            return;
        }

        float gravity;

        // Apex hang time: near the top of the arc gravity is reduced
        // so the jump feels weightless for a brief moment.
        if (Mathf.Abs(_velocity.y) < hangSpeedThreshold && _velocity.y > 0f)
            gravity = riseGravity * hangGravityMult;
        else if (_velocity.y > 0f)
            gravity = riseGravity;
        else
            gravity = fallGravity;   // fast fall on the way down

        _velocity.y -= gravity * Time.deltaTime;
    }

    // ----------------------------------------------------------
    // Horizontal movement  (acceleration-based)
    // ----------------------------------------------------------
    void MoveHorizontal()
    {
        float input = 0f;
        if (_kb.aKey.isPressed || _kb.leftArrowKey.isPressed)  input -= 1f;
        if (_kb.dKey.isPressed || _kb.rightArrowKey.isPressed) input += 1f;

        if (input >  0.01f) _gameManager.IsFacingRight = true;
        if (input < -0.01f) _gameManager.IsFacingRight = false;

        float targetSpeed = input * maxSpeed;

        // Choose accel/decel rates based on whether we have input and
        // whether the player is on the ground.
        float accelRate;
        if (IsGrounded)
            accelRate = Mathf.Abs(input) > 0.01f ? groundAcceleration : groundDeceleration;
        else
            accelRate = Mathf.Abs(input) > 0.01f ? airAcceleration    : airDeceleration;

        // Smoothly move _velocity.x toward the target speed
        _velocity.x = Mathf.MoveTowards(_velocity.x, targetSpeed, accelRate * Time.deltaTime);

        float dx = _velocity.x * Time.deltaTime;
        if (Mathf.Approximately(dx, 0f)) return;

        Vector3 newPos = Position + new Vector3(dx, 0f, 0f);
        if (CollisionManager.Instance.CheckCollision(ColliderID, newPos, out List<int> hitIds))
        {
            _velocity.x = 0f;   // kill horizontal velocity on wall hit
            HandleCollisionResponse(hitIds, wasFalling: false);
        }
        else
        {
            Position = newPos;
        }
    }

    // ----------------------------------------------------------
    // Vertical movement
    // ----------------------------------------------------------
    void MoveVertical()
    {
        float   dy     = _velocity.y * Time.deltaTime;
        Vector3 newPos = Position + new Vector3(0f, dy, 0f);

        if (CollisionManager.Instance.CheckCollision(ColliderID, newPos, out List<int> hitIds))
        {
            // Capture falling state BEFORE velocity is zeroed - needed for stomp check
            bool wasFalling = _velocity.y < -0.5f;

            if (_velocity.y < 0f) IsGrounded = true;
            _velocity.y = 0f;

            HandleCollisionResponse(hitIds, wasFalling);
        }
        else
        {
            Position   = newPos;
            IsGrounded = false;
        }
    }

    // ----------------------------------------------------------
    // Collision response
    // ----------------------------------------------------------
    void HandleCollisionResponse(List<int> hitIds, bool wasFalling)
    {
        foreach (int hitId in hitIds)
        {
            EntityType type = CollisionManager.Instance.GetEntityType(hitId);

            switch (type)
            {
                case EntityType.Enemy:
                    // Stomp: wasFalling is captured before velocity was zeroed,
                    // so this is always accurate regardless of call order.
                    if (wasFalling)
                    {
                        GetComponent<EntityManager>().RemoveEntityByColliderID(hitId);
                        _velocity.y = jumpForce * 0.65f;
                        IsGrounded  = false;
                    }
                    else if (_gameManager.IsInvincible)
                    {
                        GetComponent<EntityManager>().RemoveEntityByColliderID(hitId);
                    }
                    else
                    {
                        _gameManager.TakeDamage();
                    }
                    break;

                case EntityType.InstakillObstacle:
                    _gameManager.InstantKill();
                    break;

                case EntityType.FireballPickup:
                    _gameManager.CollectFireball();
                    GetComponent<EntityManager>().RemoveEntityByColliderID(hitId);
                    break;

                case EntityType.ExtraLifePickup:
                    _gameManager.AddLife();
                    GetComponent<EntityManager>().RemoveEntityByColliderID(hitId);
                    break;

                case EntityType.InvincibilityPickup:
                    _gameManager.CollectInvincibility();
                    GetComponent<EntityManager>().RemoveEntityByColliderID(hitId);
                    break;

                case EntityType.Goal:
                    _gameManager.TriggerWin();
                    break;
            }
        }
    }

    // ----------------------------------------------------------
    // Squash & stretch animation
    // ----------------------------------------------------------
    void UpdateVisualScale()
    {
        // Detect landing: was airborne last frame, grounded this frame
        if (!_wasGrounded && IsGrounded)
            _landTimer = 0.18f;

        // Tick timers (squashTimer kept for future use but not used for Y-squish)
        _squashTimer -= Time.deltaTime;
        _landTimer   -= Time.deltaTime;

        // ── Priority order ─────────────────────────────────────
        // IMPORTANT: Y must never go below 1 — any Y < 1 makes the player
        // appear to float above the ground because the quad is centered.
        // Only X-axis animations are used for grounded states.

        if (!IsGrounded && _velocity.y > 1f)
        {
            // Rising: tall and thin
            VisualScale = Vector3.Lerp(VisualScale, new Vector3(0.75f, 1.30f, 1f), Time.deltaTime * 20f);
        }
        else if (!IsGrounded && _velocity.y < -1f)
        {
            // Falling: slightly stretched downward
            VisualScale = Vector3.Lerp(VisualScale, new Vector3(0.85f, 1.15f, 1f), Time.deltaTime * 20f);
        }
        else if (_landTimer > 0f)
        {
            // Landing squash: only widen X — Y is always exactly 1
            // so the bottom edge stays flush with the ground.
            float n  = _landTimer / 0.18f;
            float tx = Mathf.Lerp(1f, 1.40f, n);
            VisualScale = new Vector3(tx, 1f, 1f);
        }
        else if (IsGrounded && Mathf.Abs(_velocity.x) > 0.5f)
        {
            // Walk pulse: X oscillates 1.0 → 1.05 → 1.0, Y locked at 1.
            _bobPhase += Time.deltaTime * Mathf.Abs(_velocity.x) * 7f;
            float pulse = (Mathf.Sin(_bobPhase) * 0.5f + 0.5f) * 0.05f;
            VisualScale = new Vector3(1f + pulse, 1f, 1f);
        }
        else
        {
            // Idle / base: hard-assign exact (1,1,1) every frame —
            // no lerp, no drift, guaranteed clean square like the enemies.
            VisualScale = Vector3.one;
            _bobPhase   = 0f;
        }
    }

    // ----------------------------------------------------------
    // Sync position to CollisionManager
    // ----------------------------------------------------------
    void SyncCollider()
    {
        if (ColliderID == -1) return;
        Matrix4x4 m = Matrix4x4.TRS(Position, Quaternion.identity, Vector3.one);
        CollisionManager.Instance.UpdateCollider(ColliderID, Position, ColliderSize);
        CollisionManager.Instance.UpdateMatrix(ColliderID, m);
    }

    // ----------------------------------------------------------
    // Called by GameManager on respawn (hard reset to spawn point)
    // ----------------------------------------------------------
    public void Respawn()
    {
        Position           = spawnPosition;
        _velocity          = Vector3.zero;
        IsGrounded         = false;
        _coyoteTimeCounter = 0f;
        _jumpBufferCounter = 0f;
        _squashTimer       = 0f;
        _landTimer         = 0f;
        _bobPhase          = 0f;
        VisualScale        = Vector3.one;
        SyncCollider();
    }
}