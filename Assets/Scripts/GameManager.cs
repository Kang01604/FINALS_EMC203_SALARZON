using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Player Stats")]
    public int   startingLives = 3;
    public float levelTime     = 120f;

    [Header("Powerup Settings")]
    public float invincibilityDuration   = 5f;
    [Tooltip("Brief invincibility granted after taking damage (no position reset).")]
    public float damageInvincibilityTime = 1.5f;

    public int       Lives         { get; private set; }
    public float     TimeRemaining { get; private set; }
    public bool      IsInvincible  { get; private set; }
    public bool      HasFireball   { get; private set; }
    public bool      IsFacingRight { get; set; } = true;
    public GameState CurrentState  { get; private set; } = GameState.Playing;

    [Header("Fireball")]
    [Tooltip("Minimum seconds between fireball shots.")]
    public float fireballCooldown = 0.35f;

    private float _invincibilityTimer;
    private float _fireballCooldownTimer;
    private EntityManager    _entityManager;
    private PlayerController _playerController;

    // ── State machine ──────────────────────────────────────────
    private IGameState _stateObj;

    private interface IGameState
    {
        void OnEnter(GameManager gm);
        void OnUpdate(GameManager gm);
        void OnExit(GameManager gm);
    }

    private class PlayingState : IGameState
    {
        public void OnEnter(GameManager gm)
        {
            gm.CurrentState = GameState.Playing;
            Debug.Log("[State] PLAYING");
        }
        public void OnUpdate(GameManager gm)
        {
            gm.TimeRemaining -= Time.deltaTime;
            if (gm.TimeRemaining <= 0f)
            {
                gm.TimeRemaining = 0f;
                gm.TransitionTo(new GameOverState());
                return;
            }
            if (gm.IsInvincible)
            {
                gm._invincibilityTimer -= Time.deltaTime;
                if (gm._invincibilityTimer <= 0f)
                    gm.IsInvincible = false;
            }

            if (gm._fireballCooldownTimer > 0f)
                gm._fireballCooldownTimer -= Time.deltaTime;
        }
        public void OnExit(GameManager gm) { }
    }

    private class GameOverState : IGameState
    {
        public void OnEnter(GameManager gm)
        {
            gm.CurrentState = GameState.GameOver;
            Debug.Log("[State] GAME OVER  |  R = restart   ESC = quit");
        }
        public void OnUpdate(GameManager gm)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.rKey.wasPressedThisFrame)
            {
                Debug.Log("[State] Restarting...");
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
            if (kb.escapeKey.wasPressedThisFrame)
            {
                Debug.Log("[State] Quitting...");
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }
        public void OnExit(GameManager gm) { }
    }

    private class WinState : IGameState
    {
        public void OnEnter(GameManager gm)
        {
            gm.CurrentState = GameState.Win;
            Debug.Log("[State] YOU WIN!   |  R = restart   ESC = quit");
        }
        public void OnUpdate(GameManager gm)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.rKey.wasPressedThisFrame)
            {
                Debug.Log("[State] Restarting...");
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
            if (kb.escapeKey.wasPressedThisFrame)
            {
                Debug.Log("[State] Quitting...");
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }
        public void OnExit(GameManager gm) { }
    }

    private void TransitionTo(IGameState newState)
    {
        _stateObj?.OnExit(this);
        _stateObj = newState;
        _stateObj.OnEnter(this);
    }

    // ── Unity lifecycle ────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        Lives         = startingLives;
        TimeRemaining = levelTime;
        _entityManager    = GetComponent<EntityManager>();
        _playerController = GetComponent<PlayerController>();
        TransitionTo(new PlayingState());
        _entityManager.SpawnLevel();
    }

    void Update()
    {
        _stateObj?.OnUpdate(this);
    }

    // One hit = one life lost. No HP layer.
    public void TakeDamage()
    {
        if (IsInvincible)                      return;
        if (CurrentState != GameState.Playing) return;

        HasFireball = false;
        Lives--;
        Debug.Log($"[GameManager] Hit! Lives remaining: {Lives}");

        if (Lives <= 0)
            TransitionTo(new GameOverState());
        else
        {
            _playerController.Respawn();
            GrantDamageInvincibility();
        }
    }

    // Instakill BYPASSES invincibility - spikes and pits always kill
    public void InstantKill()
    {
        if (CurrentState != GameState.Playing) return;

        HasFireball = false;
        Lives--;
        Debug.Log($"[GameManager] Instakilled! Lives remaining: {Lives}");

        if (Lives <= 0)
            TransitionTo(new GameOverState());
        else
        {
            _playerController.Respawn();
            GrantDamageInvincibility();
        }
    }

    private void GrantDamageInvincibility()
    {
        IsInvincible       = true;
        _invincibilityTimer = Mathf.Max(_invincibilityTimer, damageInvincibilityTime);
    }

    // ── Pickups ────────────────────────────────────────────────
    public void AddLife()
    {
        Lives++;
        Debug.Log($"[GameManager] Extra life! Lives: {Lives}");
    }

    public void CollectFireball()  { HasFireball = true; }

    public void CollectInvincibility()
    {
        IsInvincible       = true;
        _invincibilityTimer = invincibilityDuration;
    }

    // HasFireball is NEVER cleared here - infinite shots until player is hit
    public void UseFireball()
    {
        if (!HasFireball)                       return;
        if (CurrentState != GameState.Playing)  return;
        if (_fireballCooldownTimer > 0f)        return;
        _fireballCooldownTimer = fireballCooldown;
        float dir = IsFacingRight ? 1f : -1f;
        _entityManager.SpawnFireball(_playerController.Position, dir);
    }

    // ── Win / Lose (called externally) ────────────────────────
    public void TriggerWin()
    {
        if (CurrentState == GameState.Playing)
            TransitionTo(new WinState());
    }

    public void TriggerGameOver()
    {
        if (CurrentState == GameState.Playing)
            TransitionTo(new GameOverState());
    }
}