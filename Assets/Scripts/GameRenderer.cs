using System.Collections.Generic;
using UnityEngine;

// ============================================================
// GameRenderer  (fixed)
//
// Fixes vs original:
//  1. DrawHPIcons renamed DrawLivesIcons and now iterates
//     gm.Lives / gm.startingLives instead of CurrentHP/maxHP,
//     so the icon count decreases when a life is lost.
//  2. IsVisible() is scale-aware: it expands the culling test
//     by half the entity's world-space width/height so large
//     objects like the ground are never incorrectly culled.
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
    public float hpIconSize    = 0.4f;
    public float hpIconPadding = 0.08f;

    private GameManager      _gm;
    private EntityManager    _em;
    private PlayerController _pc;
    private PlayerCameraFollow _camFollow;
    private Camera           _cam;
    private Mesh             _quadMesh;

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

    // ── Unity lifecycle ────────────────────────────────────────
    void Awake()
    {
        _gm       = GetComponent<GameManager>();
        _em       = GetComponent<EntityManager>();
        _pc       = GetComponent<PlayerController>();
        _quadMesh = CreateQuadMesh();
    }

    void Start()
    {
        _cam       = Camera.main;
        _camFollow = _cam != null ? _cam.GetComponent<PlayerCameraFollow>() : null;
    }

    void Update()
    {
        if (_camFollow != null)
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
    // FIX: accepts the entity's world-space scale so large objects
    // (like the 200-unit-wide ground) are never incorrectly culled.
    bool IsVisible(Vector3 worldPos, Vector3 worldScale)
    {
        if (_cam == null) return true;

        Vector3 toObj  = worldPos - _cam.transform.position;
        float   fwdDot = Vector3.Dot(toObj, _cam.transform.forward);
        if (fwdDot < -cullingMargin) return false;

        // Expand the acceptance zone by half the object's size on each axis
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
        if (_pc.ColliderID == -1 || playerMaterial == null) return;
        if (_gm.IsInvincible && (Time.time % 0.2f < 0.1f)) return;

        Matrix4x4 m = Matrix4x4.TRS(_pc.Position, Quaternion.identity, _pc.VisualScale);
        Graphics.DrawMesh(_quadMesh, m, playerMaterial, 0);
    }

    // ── World entities ─────────────────────────────────────────
    void DrawWorld()
    {
        var buckets = new Dictionary<EntityType, List<Matrix4x4>>();

        foreach (var entity in _em.GetActiveEntities())
        {
            // FIX: pass entity.scale so large ground/platform tiles are never culled
            if (!IsVisible(entity.position, entity.scale)) continue;

            if (!buckets.ContainsKey(entity.type))
                buckets[entity.type] = new List<Matrix4x4>();

            buckets[entity.type].Add(
                Matrix4x4.TRS(entity.position, Quaternion.identity, entity.scale));
        }

        DrawBucket(buckets, EntityType.Ground,              groundMaterial);
        DrawBucket(buckets, EntityType.Obstacle,            obstacleMaterial);
        DrawBucket(buckets, EntityType.InstakillObstacle,   instakillMaterial);
        DrawBucket(buckets, EntityType.Enemy,               enemyMaterial);
        DrawBucket(buckets, EntityType.Fireball,            fireballProjectileMaterial);
        DrawBucket(buckets, EntityType.FireballPickup,      fireballPickupMaterial);
        DrawBucket(buckets, EntityType.ExtraLifePickup,     extraLifeMaterial);
        DrawBucket(buckets, EntityType.InvincibilityPickup, invincibilityMaterial);
        DrawBucket(buckets, EntityType.Goal,                goalMaterial);
    }

    void DrawBucket(Dictionary<EntityType, List<Matrix4x4>> buckets,
                    EntityType type, Material mat)
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
            Graphics.DrawMeshInstanced(_quadMesh, 0, mat, batch, count);
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
        DrawLivesIcons();
        DrawTimer();
    }

    // FIX: Shows LIVES count (not HP). Icons decrease when a life is lost.
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

            // i < Lives = filled icon; otherwise empty
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
            Graphics.DrawMeshInstanced(_quadMesh, 0, segmentMaterial,
                                       segMatrices.ToArray(), segMatrices.Count);
    }
}