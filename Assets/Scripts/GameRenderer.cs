using System.Collections.Generic;
using UnityEngine;

// ============================================================
// GameRenderer
// ============================================================
public class GameRenderer : MonoBehaviour
{
    [Header("World Materials")]
    public Material playerMaterial;
    public Material enemyMaterial;
    public Material groundMaterial;
    public Material obstacleMaterial;
    public Material instakillMaterial;
    public Material goalMaterial;

    [Header("Pickup Materials")]
    public Material fireballPickupMaterial;
    public Material extraLifeMaterial;
    public Material invincibilityMaterial;
    public Material fireballProjectileMaterial;

    [Header("UI Materials")]
    public Material hpIconMaterial;
    public Material hpIconEmptyMaterial;
    public Material segmentMaterial;

    [Header("Camera Culling")]
    public float cullingMargin = 3f;

    [Header("UI Layout")]
    public float uiDepthOffset = 0.5f;
    public float uiScale       = 0.5f;
    public float hpIconSize    = 0.75f;
    public float hpIconPadding = 0.20f;

    private GameManager        _gm;
    private EntityManager      _em;
    private PlayerController   _pc;
    private PlayerCameraFollow _camFollow;
    private Camera             _cam;
    private Mesh               _quadMesh;

    private MaterialPropertyBlock _playerMpb;
    private MaterialPropertyBlock _instakillMpb;
    
    // Exact colors for the Lava effect
    private Color _lavaBaseColor;
    private Color _lavaUpColor;
    private Color _lavaDownColor;

    // ── 7-Segment display constants ───────────────────────────
    private const float DW  = 0.6f;
    private const float DH  = 1.0f;
    private const float ST  = 0.12f;
    private const float DSP = 0.85f;

    private static readonly bool[][] DIGIT_SEG = new bool[10][]
    {
        new bool[]{true,  true,  true,  false,true,  true,  true },  // 0
        new bool[]{false, false, true,  false,false, true,  false},  // 1
        new bool[]{true,  false, true,  true, true,  false, true },  // 2
        new bool[]{true,  false, true,  true, false, true,  true },  // 3
        new bool[]{false, true,  true,  true, false, true,  false},  // 4
        new bool[]{true,  true,  false, true, false, true,  true },  // 5
        new bool[]{true,  true,  false, true, true,  true,  true },  // 6
        new bool[]{true,  false, true,  false,false, true,  false},  // 7
        new bool[]{true,  true,  true,  true, true,  true,  true },  // 8
        new bool[]{true,  true,  true,  true, false, true,  true },  // 9
    };

    private static Vector3[] SEG_POS => new Vector3[]
    {
        new Vector3( 0f,       DH*0.5f,  0f),
        new Vector3(-DW*0.5f,  DH*0.25f, 0f),
        new Vector3( DW*0.5f,  DH*0.25f, 0f),
        new Vector3( 0f,       0f,       0f),
        new Vector3(-DW*0.5f, -DH*0.25f, 0f),
        new Vector3( DW*0.5f, -DH*0.25f, 0f),
        new Vector3( 0f,      -DH*0.5f,  0f),
    };

    private static Vector3[] SEG_SCALE => new Vector3[]
    {
        new Vector3(DW,  ST,         0.1f),
        new Vector3(ST,  DH*0.45f,   0.1f),
        new Vector3(ST,  DH*0.45f,   0.1f),
        new Vector3(DW,  ST,         0.1f),
        new Vector3(ST,  DH*0.45f,   0.1f),
        new Vector3(ST,  DH*0.45f,   0.1f),
        new Vector3(DW,  ST,         0.1f),
    };

    // ── 3x5 Custom Text Rendering Font ──────────────────────────
    private static readonly Dictionary<char, string[]> FONT = new Dictionary<char, string[]>
    {
        {'A', new string[]{"111","101","111","101","101"}},
        {'B', new string[]{"110","101","110","101","110"}},
        {'C', new string[]{"111","100","100","100","111"}},
        {'D', new string[]{"110","101","101","101","110"}},
        {'E', new string[]{"111","100","111","100","111"}},
        {'F', new string[]{"111","100","111","100","100"}},
        {'G', new string[]{"111","100","101","101","111"}},
        {'H', new string[]{"101","101","111","101","101"}},
        {'I', new string[]{"111","010","010","010","111"}},
        {'J', new string[]{"001","001","001","101","111"}},
        {'K', new string[]{"101","110","100","110","101"}},
        {'L', new string[]{"100","100","100","100","111"}},
        {'M', new string[]{"101","111","101","101","101"}},
        {'N', new string[]{"110","101","101","101","101"}},
        {'O', new string[]{"111","101","101","101","111"}},
        {'P', new string[]{"111","101","111","100","100"}},
        {'Q', new string[]{"111","101","101","111","001"}},
        {'R', new string[]{"111","101","111","110","101"}},
        {'S', new string[]{"111","100","111","001","111"}},
        {'T', new string[]{"111","010","010","010","010"}},
        {'U', new string[]{"101","101","101","101","111"}},
        {'V', new string[]{"101","101","101","101","010"}},
        {'W', new string[]{"101","101","101","111","101"}},
        {'X', new string[]{"101","101","010","101","101"}},
        {'Y', new string[]{"101","101","010","010","010"}},
        {'Z', new string[]{"111","001","010","100","111"}},
        {'!', new string[]{"010","010","010","000","010"}},
        {'\'', new string[]{"010","010","000","000","000"}},
        {'|', new string[]{"010","010","010","010","010"}},
        {' ', new string[]{"000","000","000","000","000"}},
        {'=', new string[]{"000","111","000","111","000"}}
    };

    // ── Unity lifecycle ────────────────────────────────────────
    void Awake()
    {
        _gm       = GetComponent<GameManager>();
        _em       = GetComponent<EntityManager>();
        _pc       = GetComponent<PlayerController>();
        _quadMesh = CreateQuadMesh();
        
        _playerMpb    = new MaterialPropertyBlock();
        _instakillMpb = new MaterialPropertyBlock();

        // Parse your specific hex colors into Unity Colors
        ColorUtility.TryParseHtmlString("#FF6517", out _lavaBaseColor);
        ColorUtility.TryParseHtmlString("#FF8E17", out _lavaUpColor);
        ColorUtility.TryParseHtmlString("#FF5717", out _lavaDownColor);
    }

    void Start()
    {
        _cam       = Camera.main;
        _camFollow = _cam != null ? _cam.GetComponent<PlayerCameraFollow>() : null;
    }

    void Update()
    {
        if (_camFollow != null && _gm.CurrentState != GameState.GameOver)
            _camFollow.SetPlayerPosition(_pc.Position);

        DrawWorld();
        DrawPlayer();
        DrawUI();
    }

    // ── Mesh ───────────────────────────────────────────────────
    Mesh CreateQuadMesh()
    {
        Mesh m = new Mesh { name = "GameQuad" };
        m.vertices  = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3( 0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f), new Vector3( 0.5f,  0.5f, 0f),
        };
        m.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
        m.uv        = new Vector2[]
        {
            new Vector2(0,0), new Vector2(1,0),
            new Vector2(0,1), new Vector2(1,1),
        };
        m.normals = new Vector3[]
        {
            Vector3.forward, Vector3.forward,
            Vector3.forward, Vector3.forward,
        };
        m.RecalculateBounds();
        return m;
    }

    // ── Culling ────────────────────────────────────────────────
    bool IsVisible(Vector3 worldPos, Vector3 worldScale)
    {
        if (_cam == null) return true;

        Vector3 toObj  = worldPos - _cam.transform.position;
        float   fwdDot = Vector3.Dot(toObj, _cam.transform.forward);
        if (fwdDot < -cullingMargin) return false;

        float halfW = _cam.orthographicSize * _cam.aspect + cullingMargin + worldScale.x * 0.5f;
        float halfH = _cam.orthographicSize              + cullingMargin + worldScale.y * 0.5f;

        float rightDot = Vector3.Dot(toObj, _cam.transform.right);
        float upDot    = Vector3.Dot(toObj, _cam.transform.up);

        return Mathf.Abs(rightDot) <= halfW &&
               Mathf.Abs(upDot)   <= halfH;
    }

    // ── Player ─────────────────────────────────────────────────
    void DrawPlayer()
    {
        if (playerMaterial == null) return;
        
        bool isDamageInvincible = _gm.IsInvincible && !_gm.IsPowerupInvincible;
        
        if (isDamageInvincible && (Time.time % 0.2f < 0.1f)) return;

        Vector3 currentScale = _pc.VisualScale;

        // Visual pulsing animations
        if (_gm.IsShowingExtraLifeEffect)
        {
            float pulse = (Mathf.Sin(Time.time * 20f) * 0.5f + 0.5f) * 0.25f; 
            currentScale += new Vector3(pulse, pulse, 0f);
        }
        else if (_gm.HasFireball)
        {
            float pulse = (Mathf.Sin(Time.time * 8f) * 0.5f + 0.5f) * 0.15f; 
            currentScale += new Vector3(pulse, pulse, 0f);
        }

        Matrix4x4 m = Matrix4x4.TRS(_pc.Position, Quaternion.identity, currentScale);

        bool overrideColor = false;
        Color targetColor = Color.white;

        if (_gm.IsShowingExtraLifeEffect)
        {
            targetColor = Color.green;
            overrideColor = true;
        }
        else if (_gm.IsPowerupInvincible)
        {
            Color powerupColor = Color.magenta; 
            if (invincibilityMaterial != null)
            {
                if (invincibilityMaterial.HasProperty("_Color"))
                    powerupColor = invincibilityMaterial.GetColor("_Color");
                else if (invincibilityMaterial.HasProperty("_BaseColor"))
                    powerupColor = invincibilityMaterial.GetColor("_BaseColor");
            }

            // Figure out what the player's natural color *would* be right now
            // If they have the fireball, their base is ORANGE. Otherwise, it is WHITE.
            Color baseStateColor = _gm.HasFireball ? new Color(1.0f, 0.45f, 0.0f) : Color.white;
            
            // Flash between purple and the current proper state color rapidly when ending
            if (_gm.InvincibilityTimeRemaining <= 1.5f && (Time.time % 0.2f < 0.1f))
            {
                targetColor = baseStateColor;
            }
            else
            {
                targetColor = Color.Lerp(powerupColor, baseStateColor, 0.2f);
            }
            
            overrideColor = true;
        }
        else if (isDamageInvincible)
        {
            targetColor = Color.red;
            overrideColor = true;
        }
        else if (_gm.HasFireball)
        {
            targetColor = new Color(1.0f, 0.45f, 0.0f); 
            overrideColor = true;
        }

        if (overrideColor)
        {
            _playerMpb.SetColor("_Color", targetColor);
            _playerMpb.SetColor("_BaseColor", targetColor);
            Graphics.DrawMesh(_quadMesh, m, playerMaterial, 0, null, 0, _playerMpb);
        }
        else
        {
            Graphics.DrawMesh(_quadMesh, m, playerMaterial, 0);
        }
    }

    // ── World entities ─────────────────────────────────────────
    void DrawWorld()
    {
        var buckets = new Dictionary<EntityType, List<Matrix4x4>>();

        foreach (var entity in _em.GetActiveEntities())
        {
            if (!IsVisible(entity.position, entity.scale)) continue;

            if (!buckets.ContainsKey(entity.type))
                buckets[entity.type] = new List<Matrix4x4>();

            buckets[entity.type].Add(
                Matrix4x4.TRS(entity.position, Quaternion.identity, entity.scale));
        }
        
        // Setup Lava Color Interpolation for Instakill Objects
        float lavaPulse = Mathf.Sin(Time.time * 3f); 
        Color shiftedLavaColor;
        
        if (lavaPulse > 0f)
            shiftedLavaColor = Color.Lerp(_lavaBaseColor, _lavaUpColor, lavaPulse);
        else
            shiftedLavaColor = Color.Lerp(_lavaBaseColor, _lavaDownColor, -lavaPulse);
        
        _instakillMpb.SetColor("_Color", shiftedLavaColor);
        _instakillMpb.SetColor("_BaseColor", shiftedLavaColor);

        // Draw normal world buckets
        DrawBucket(buckets, EntityType.Ground,              groundMaterial);
        DrawBucket(buckets, EntityType.Obstacle,            obstacleMaterial);
        DrawBucket(buckets, EntityType.InstakillObstacle,   instakillMaterial, _instakillMpb); 
        DrawBucket(buckets, EntityType.Enemy,               enemyMaterial);
        DrawBucket(buckets, EntityType.Fireball,            fireballProjectileMaterial);
        DrawBucket(buckets, EntityType.FireballPickup,      fireballPickupMaterial);
        DrawBucket(buckets, EntityType.ExtraLifePickup,     extraLifeMaterial);
        DrawBucket(buckets, EntityType.InvincibilityPickup, invincibilityMaterial);
        DrawBucket(buckets, EntityType.Goal,                goalMaterial);

        // --- DRAW THE NEW WORLD SPACE TEXTS ---
        if (_gm != null)
        {
            // Start Text
            DrawWorldString("GAME START!", _gm.PlayerSpawnPosition + new Vector3(0, 4.5f, 0), 0.15f, Color.white);
            DrawWorldString("|",           _gm.PlayerSpawnPosition + new Vector3(0, 3.2f, 0), 0.15f, Color.white);
            DrawWorldString("V",           _gm.PlayerSpawnPosition + new Vector3(0, 1.9f, 0), 0.15f, Color.white);

            // Controls hint - left of spawn, outside player boundary, left-aligned
            Vector3 ctrlBase = _gm.PlayerSpawnPosition + new Vector3(-8f, -0.5f, 0);
            DrawWorldString("CONTROLS",        ctrlBase + new Vector3(0,  0.0f, 0), 0.08f, new Color(0.7f, 0.7f, 0.7f), leftAlign: true);
            DrawWorldString("A = LEFT",        ctrlBase + new Vector3(0, -0.6f, 0), 0.08f, new Color(0.6f, 0.6f, 0.6f), leftAlign: true);
            DrawWorldString("D = RIGHT",       ctrlBase + new Vector3(0, -1.2f, 0), 0.08f, new Color(0.6f, 0.6f, 0.6f), leftAlign: true);
            DrawWorldString("SPACE = JUMP",    ctrlBase + new Vector3(0, -1.8f, 0), 0.08f, new Color(0.6f, 0.6f, 0.6f), leftAlign: true);
            DrawWorldString("F = USE ABILITY", ctrlBase + new Vector3(0, -2.4f, 0), 0.08f, new Color(0.6f, 0.6f, 0.6f), leftAlign: true);

            // End Text
            DrawWorldString("END GOAL", _gm.GoalPosition + new Vector3(0, 4.5f, 0), 0.15f, Color.yellow);
            DrawWorldString("|",        _gm.GoalPosition + new Vector3(0, 3.2f, 0), 0.15f, Color.yellow);
            DrawWorldString("V",        _gm.GoalPosition + new Vector3(0, 1.9f, 0), 0.15f, Color.yellow);
        }
    }

    void DrawBucket(Dictionary<EntityType, List<Matrix4x4>> buckets, EntityType type, Material mat, MaterialPropertyBlock mpb = null)
    {
        if (mat == null || !buckets.ContainsKey(type)) return;
        var list = buckets[type];
        if (list.Count == 0) return;
        Matrix4x4[] arr = list.ToArray();
        for (int i = 0; i < arr.Length; i += 1023)
        {
            int count = Mathf.Min(1023, arr.Length - i);
            Matrix4x4[] batch = new Matrix4x4[count];
            System.Array.Copy(arr, i, batch, 0, count);
            Graphics.DrawMeshInstanced(_quadMesh, 0, mat, batch, count, mpb);
        }
    }

    // ── Custom String Renderer (WORLD SPACE) ───────────────────
    void DrawWorldString(string text, Vector3 centerWorldPos, float size, Color color, bool leftAlign = false)
    {
        if (segmentMaterial == null) return;

        text = text.ToUpper();
        var matrices = new List<Matrix4x4>();

        float charSpacing = 4f * size;
        float totalWidth  = text.Length * charSpacing - size;
        
        Vector3 right    = Vector3.right;
        Vector3 up       = Vector3.up;
        Vector3 startPos = leftAlign
            ? centerWorldPos
            : centerWorldPos - right * (totalWidth * 0.5f);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!FONT.ContainsKey(c)) c = ' ';

            string[] rows = FONT[c];
            Vector3 charOrigin = startPos + right * (i * charSpacing);

            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    if (rows[y][x] == '1')
                    {
                        Vector3 blockPos = charOrigin + right * (x * size) + up * ((2 - y) * size);
                        Vector3 blockScale = new Vector3(size, size, 0.1f);
                        matrices.Add(Matrix4x4.TRS(blockPos, Quaternion.identity, blockScale));
                    }
                }
            }
        }

        if (matrices.Count > 0)
        {
            _playerMpb.SetColor("_Color", color);
            _playerMpb.SetColor("_BaseColor", color);
            for (int i = 0; i < matrices.Count; i += 1023)
            {
                int count = Mathf.Min(1023, matrices.Count - i);
                Matrix4x4[] batch = new Matrix4x4[count];
                System.Array.Copy(matrices.ToArray(), i, batch, 0, count);
                Graphics.DrawMeshInstanced(_quadMesh, 0, segmentMaterial, batch, count, _playerMpb);
            }
        }
    }

    // ── UI ─────────────────────────────────────────────────────
    Vector3 ScreenToWorld(float nx, float ny)
    {
        if (_cam == null) return Vector3.zero;
        float halfH = _cam.orthographicSize;
        float halfW = halfH * _cam.aspect;
        return _cam.transform.position
             + _cam.transform.right   * ((nx * 2f - 1f) * halfW)
             + _cam.transform.up      * ((ny * 2f - 1f) * halfH)
             + _cam.transform.forward * uiDepthOffset;
    }

    void DrawUI()
    {
        if (_gm.CurrentState == GameState.Playing)
        {
            DrawLivesIcons();
            DrawTimer();
        }
        else if (_gm.CurrentState == GameState.Win)
        {
            DrawWinScreen();
        }
        else if (_gm.CurrentState == GameState.GameOver)
        {
            DrawGameOverScreen();
        }
    }

    void DrawWinScreen()
    {
        Vector3 bgPos = ScreenToWorld(0.5f, 0.5f) + _cam.transform.forward * 0.2f; 
        Matrix4x4 bgMat = Matrix4x4.TRS(bgPos, Quaternion.identity, new Vector3(100f, 100f, 1f));
        
        _playerMpb.SetColor("_Color", new Color(0, 0, 0, 0.75f));
        _playerMpb.SetColor("_BaseColor", new Color(0, 0, 0, 0.75f));
        Graphics.DrawMesh(_quadMesh, bgMat, segmentMaterial, 0, null, 0, _playerMpb);

        if (Time.time % 0.8f < 0.4f)
        {
            Vector3 center = ScreenToWorld(0.5f, 0.65f);
            DrawWorldString("YOU WIN!", center, 0.15f, Color.yellow);
        }

        Vector3 restartCenter = ScreenToWorld(0.5f, 0.35f);
        DrawWorldString("PRESS 'R' TO RESTART", restartCenter, 0.08f, Color.white);
    }

    void DrawGameOverScreen()
    {
        Vector3 restartCenter = ScreenToWorld(0.5f, 0.65f);
        DrawWorldString("PRESS 'R' TO RESTART", restartCenter, 0.08f, Color.white);
    }

    void DrawLivesIcons()
    {
        if (hpIconMaterial == null) return;

        float step   = (hpIconSize + hpIconPadding) * uiScale;
        var active   = new List<Matrix4x4>();
        var inactive = new List<Matrix4x4>();

        for (int i = 0; i < _gm.startingLives; i++)
        {
            Vector3 origin = ScreenToWorld(0.03f, 0.94f);
            Vector3 pos    = origin + _cam.transform.right * i * step;
            Vector3 scl    = new Vector3(hpIconSize * uiScale,
                                          hpIconSize * uiScale, 0.1f);

            if (i < _gm.Lives)
                active.Add(Matrix4x4.TRS(pos, Quaternion.identity, scl));
            else
                inactive.Add(Matrix4x4.TRS(pos, Quaternion.identity, scl));
        }

        if (active.Count   > 0 && hpIconMaterial      != null)
            Graphics.DrawMeshInstanced(_quadMesh, 0, hpIconMaterial,
                                       active.ToArray(),   active.Count);
        if (inactive.Count > 0 && hpIconEmptyMaterial  != null)
            Graphics.DrawMeshInstanced(_quadMesh, 0, hpIconEmptyMaterial,
                                       inactive.ToArray(), inactive.Count);
    }

    void DrawTimer()
    {
        if (segmentMaterial == null) return;

        int seconds = Mathf.Clamp(Mathf.CeilToInt(_gm.TimeRemaining), 0, 9999);

        int[] digits = seconds >= 1000
            ? new int[]{ seconds/1000%10, seconds/100%10, seconds/10%10, seconds%10 }
            : seconds >= 100
              ? new int[]{ seconds/100%10, seconds/10%10, seconds%10 }
              : new int[]{ seconds/10%10, seconds%10 };

        var segMatrices = new List<Matrix4x4>();
        float   s       = uiScale * 0.8f;
        float   startNX = 1f - (digits.Length * DSP * s / (_cam.orthographicSize * _cam.aspect * 2f)) - 0.02f;
        Vector3 origin  = ScreenToWorld(startNX, 0.94f);
        Vector3 right   = _cam.transform.right;
        Vector3 up      = _cam.transform.up;
        Vector3[] sPos  = SEG_POS;
        Vector3[] sScl  = SEG_SCALE;

        for (int d = 0; d < digits.Length; d++)
        {
            Vector3 dOrigin = origin + right * d * DSP * s;
            bool[]  pattern = DIGIT_SEG[digits[d]];

            for (int seg = 0; seg < 7; seg++)
            {
                if (!pattern[seg])
                {
                    segMatrices.Add(Matrix4x4.TRS(dOrigin, Quaternion.identity, Vector3.zero));
                    continue;
                }
                Vector3 wPos = dOrigin + right * sPos[seg].x * s + up * sPos[seg].y * s;
                Vector3 wScl = new Vector3(sScl[seg].x * s, sScl[seg].y * s, sScl[seg].z);
                segMatrices.Add(Matrix4x4.TRS(wPos, Quaternion.identity, wScl));
            }
        }

        if (segMatrices.Count > 0)
        {
            _playerMpb.Clear();
            Graphics.DrawMeshInstanced(_quadMesh, 0, segmentMaterial,
                                       segMatrices.ToArray(), segMatrices.Count, _playerMpb);
        }
    }
}