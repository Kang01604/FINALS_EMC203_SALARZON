using System.Collections.Generic;
using UnityEngine;

// ============================================================
// EntityType & GameState enums - used across all scripts
// ============================================================
public enum EntityType
{
    Player,
    Ground,
    Enemy,
    Obstacle,
    InstakillObstacle,
    FireballPickup,
    ExtraLifePickup,
    InvincibilityPickup,
    Fireball,
    Goal
}

public enum GameState
{
    Playing,
    GameOver,
    Win
}

// ============================================================
// AABBBounds - axis-aligned bounding box for one collider
// ============================================================
public class AABBBounds
{
    public Vector3 Center   { get; private set; }
    public Vector3 Size     { get; private set; }
    public Vector3 Extents  { get; private set; }
    public Vector3 Min      { get; private set; }
    public Vector3 Max      { get; private set; }
    public int     ID       { get; private set; }
    public bool    IsPlayer { get; private set; }
    public EntityType Type  { get; set; }
    public Matrix4x4 Matrix { get; set; }

    public AABBBounds(Vector3 center, Vector3 size, int id,
                      bool isPlayer = false,
                      EntityType type = EntityType.Obstacle)
    {
        ID       = id;
        IsPlayer = isPlayer;
        Type     = type;
        UpdateBounds(center, size);
    }

    public void UpdateBounds(Vector3 center, Vector3 size)
    {
        Center  = center;
        Size    = size;
        Extents = size * 0.5f;
        Min     = center - Extents;
        Max     = center + Extents;
    }

    public bool Intersects(AABBBounds other)
    {
        return !(Max.x < other.Min.x || Min.x > other.Max.x ||
                 Max.y < other.Min.y || Min.y > other.Max.y ||
                 Max.z < other.Min.z || Min.z > other.Max.z);
    }
}

// ============================================================
// CollisionManager - singleton, lives on the Manager GameObject
// ============================================================
public class CollisionManager : MonoBehaviour
{
    private static CollisionManager _instance;
    public static CollisionManager Instance
    {
        get
        {
            if (_instance == null)
                Debug.LogError("CollisionManager not found! Make sure it is on the Manager GameObject.");
            return _instance;
        }
    }

    private Dictionary<int, AABBBounds> _colliders = new Dictionary<int, AABBBounds>();
    private int _nextID = 0;

    void Awake()
    {
        // Claim singleton slot - no DontDestroyOnLoad needed since
        // we are a component on the persistent Manager object.
        _instance = this;
    }

    // ----------------------------------------------------------
    // Registration
    // ----------------------------------------------------------
    public int RegisterCollider(Vector3 center, Vector3 size,
                                bool isPlayer = false,
                                EntityType type = EntityType.Obstacle)
    {
        int id = _nextID++;
        _colliders[id] = new AABBBounds(center, size, id, isPlayer, type);
        return id;
    }

    public void RemoveCollider(int id)
    {
        _colliders.Remove(id);
    }

    // ----------------------------------------------------------
    // Updates
    // ----------------------------------------------------------
    public void UpdateCollider(int id, Vector3 center, Vector3 size)
    {
        if (_colliders.TryGetValue(id, out AABBBounds b))
            b.UpdateBounds(center, size);
    }

    public void UpdateMatrix(int id, Matrix4x4 matrix)
    {
        if (_colliders.TryGetValue(id, out AABBBounds b))
            b.Matrix = matrix;
    }

    // ----------------------------------------------------------
    // Queries
    // ----------------------------------------------------------
    /// <summary>
    /// Returns true if the collider at 'id' would overlap anything
    /// if moved to 'newCenter'. collidingIds contains all hit IDs.
    /// </summary>
    public bool CheckCollision(int id, Vector3 newCenter, out List<int> collidingIds)
    {
        collidingIds = new List<int>();

        if (!_colliders.TryGetValue(id, out AABBBounds current))
            return false;

        // Temporary bounds at the proposed position
        AABBBounds temp = new AABBBounds(newCenter, current.Size, -1);

        bool hit = false;
        foreach (var kvp in _colliders)
        {
            if (kvp.Key == id) continue;          // skip self
            if (temp.Intersects(kvp.Value))
            {
                collidingIds.Add(kvp.Key);
                hit = true;
            }
        }
        return hit;
    }

    public EntityType GetEntityType(int id)
    {
        return _colliders.TryGetValue(id, out AABBBounds b) ? b.Type : EntityType.Obstacle;
    }

    public AABBBounds GetBounds(int id)
    {
        _colliders.TryGetValue(id, out AABBBounds b);
        return b;
    }

    public Matrix4x4 GetMatrix(int id)
    {
        return _colliders.TryGetValue(id, out AABBBounds b) ? b.Matrix : Matrix4x4.identity;
    }
}
