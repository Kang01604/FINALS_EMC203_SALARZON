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

    [Header("Level Boundaries")]
    [Tooltip("Keep the player within the Start and Goal dynamically.")]
    public bool  useLevelBounds = true;

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
    private bool  _isDead;

    // ── Squash & stretch animation ───────────────────────────
    private const float JumpSquashDuration = 0.12f;
    private float   _squashTimer;   
    private float   _landTimer;     
    private bool    _wasGrounded;   
    private float   _bobPhase;      

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

        Position = _gameManager.PlayerSpawnPosition;

        ColliderID = CollisionManager.Instance.RegisterCollider(
            Position, ColliderSize, true, EntityType.Player);

        SyncCollider();
    }

    void Update()
    {
        _kb = Keyboard.current;

        if (_gameManager.CurrentState == GameState.GameOver)
        {
            if (_isDead)
            {
                _velocity.y -= fallGravity * Time.deltaTime;
                Position += new Vector3(0f, _velocity.y * Time.deltaTime, 0f);
            }
            return;
        }

        if (_kb == null)                                    return;
        if (_gameManager.CurrentState != GameState.Playing) return;

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

        bool canJump = _jumpBufferCounter > 0f &&
                       (IsGrounded || _coyoteTimeCounter > 0f);
        if (canJump)
        {
            _velocity.y        = jumpForce;
            IsGrounded         = false;
            _coyoteTimeCounter = 0f;
            _jumpBufferCounter = 0f;
            _squashTimer       = JumpSquashDuration;
        }

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
        if (Mathf.Abs(_velocity.y) < hangSpeedThreshold && _velocity.y > 0f)
            gravity = riseGravity * hangGravityMult;
        else if (_velocity.y > 0f)
            gravity = riseGravity;
        else
            gravity = fallGravity;

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

        float accelRate;
        if (IsGrounded)
            accelRate = Mathf.Abs(input) > 0.01f ? groundAcceleration : groundDeceleration;
        else
            accelRate = Mathf.Abs(input) > 0.01f ? airAcceleration    : airDeceleration;

        _velocity.x = Mathf.MoveTowards(_velocity.x, targetSpeed, accelRate * Time.deltaTime);

        float dx = _velocity.x * Time.deltaTime;
        if (Mathf.Approximately(dx, 0f)) return;

        Vector3 newPos = Position + new Vector3(dx, 0f, 0f);
        if (CollisionManager.Instance.CheckCollision(ColliderID, newPos, out List<int> hitIds))
        {
            bool hitSolid = false;
            foreach (int id in hitIds)
            {
                EntityType type = CollisionManager.Instance.GetEntityType(id);
                if (type == EntityType.Ground || type == EntityType.Obstacle || type == EntityType.InstakillObstacle)
                {
                    hitSolid = true;
                }
            }

            HandleCollisionResponse(hitIds, wasFalling: false);

            if (hitSolid)
            {
                _velocity.x = 0f; 
            }
            else
            {
                Position = newPos; 
            }
        }
        else
        {
            Position = newPos;
        }

        // ==========================================
        // DYNAMIC BOUNDARIES
        // ==========================================
        if (useLevelBounds)
        {
            float dynamicMinX = _gameManager.PlayerSpawnPosition.x - 1.5f;

            // Find the actual goal entity position so inspector stale values can't break this
            float dynamicMaxX = _gameManager.GoalPosition.x + 1f;
            foreach (var entity in GetComponent<EntityManager>().GetActiveEntities())
            {
                if (entity.type == EntityType.Goal)
                {
                    dynamicMaxX = entity.position.x + 1f;
                    break;
                }
            }

            float clampedX = Mathf.Clamp(Position.x, dynamicMinX, dynamicMaxX);
            if (!Mathf.Approximately(Position.x, clampedX))
            {
                Position = new Vector3(clampedX, Position.y, Position.z);
                _velocity.x = 0f;
            }
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
            bool hitSolid = false;
            foreach (int id in hitIds)
            {
                EntityType type = CollisionManager.Instance.GetEntityType(id);
                if (type == EntityType.Ground || type == EntityType.Obstacle || type == EntityType.InstakillObstacle)
                {
                    hitSolid = true;
                }
            }

            bool wasFalling = _velocity.y < -0.5f;

            if (hitSolid)
            {
                if (_velocity.y < 0f) IsGrounded = true;
                _velocity.y = 0f; 
            }
            else
            {
                Position = newPos; 
            }

            HandleCollisionResponse(hitIds, wasFalling);
        }
        else
        {
            Position = newPos;

            bool groundBelow = CollisionManager.Instance.CheckCollision(
                ColliderID, 
                Position + new Vector3(0f, -0.08f, 0f), 
                out List<int> belowIds
            );

            bool solidBelow = false;
            if (groundBelow)
            {
                foreach (int id in belowIds)
                {
                    EntityType type = CollisionManager.Instance.GetEntityType(id);
                    if (type == EntityType.Ground || type == EntityType.Obstacle || type == EntityType.InstakillObstacle)
                    {
                        solidBelow = true;
                    }
                }
            }

            if (!solidBelow)
            {
                IsGrounded = false;
            }
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
                    if (wasFalling)
                    {
                        GetComponent<EntityManager>().RemoveEntityByColliderID(hitId);
                        _velocity.y = jumpForce * 0.65f;
                        IsGrounded  = false;
                    }
                    else if (_gameManager.IsPowerupInvincible)
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
        if (!_wasGrounded && IsGrounded && _landTimer <= 0f)
            _landTimer = 0.18f;

        _squashTimer -= Time.deltaTime;
        _landTimer   -= Time.deltaTime;

        if (!IsGrounded && _velocity.y > 1f)
        {
            VisualScale = Vector3.Lerp(VisualScale, new Vector3(0.75f, 1.30f, 1f), Time.deltaTime * 20f);
        }
        else if (!IsGrounded && _velocity.y < -1f)
        {
            VisualScale = Vector3.Lerp(VisualScale, new Vector3(0.85f, 1.15f, 1f), Time.deltaTime * 20f);
        }
        else if (_landTimer > 0f)
        {
            float n  = _landTimer / 0.18f;
            float tx = Mathf.Lerp(1f, 1.40f, n);
            VisualScale = new Vector3(tx, 1f, 1f);
        }
        else if (IsGrounded && Mathf.Abs(_velocity.x) > 0.5f)
        {
            _bobPhase += Time.deltaTime * Mathf.Abs(_velocity.x) * 7f;
            float pulse = (Mathf.Sin(_bobPhase) * 0.5f + 0.5f) * 0.05f;
            VisualScale = new Vector3(1f + pulse, 1f, 1f);
        }
        else
        {
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

    public void TriggerDeathAnimation()
    {
        _isDead = true;
        _velocity = new Vector3(0f, jumpForce * 0.8f, 0f);
        
        if (ColliderID != -1)
        {
            CollisionManager.Instance.RemoveCollider(ColliderID);
            ColliderID = -1;
        }
    }

    public void Respawn()
    {
        _isDead            = false;
        Position           = _gameManager.PlayerSpawnPosition;
        _velocity          = Vector3.zero;
        IsGrounded         = false;
        _coyoteTimeCounter = 0f;
        _jumpBufferCounter = 0f;
        _squashTimer       = 0f;
        _landTimer         = 0f;
        _bobPhase          = 0f;
        VisualScale        = Vector3.one;
        
        if (ColliderID == -1)
        {
            ColliderID = CollisionManager.Instance.RegisterCollider(
                Position, ColliderSize, true, EntityType.Player);
        }
        
        SyncCollider();
    }
}