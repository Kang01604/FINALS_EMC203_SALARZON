using System.Collections.Generic;
using UnityEngine;

// ============================================================
// EntityData - plain data class for one game entity
// ============================================================
public class EntityData
{
    public int        colliderID;
    public EntityType type;
    public Vector3    position;
    public Vector3    velocity;
    public Vector3    scale;
    public float      patrolMinX;
    public float      patrolMaxX;
    public bool       isActive;
    public float      distanceTravelled;
    public float      maxDistance;

    public EntityData(int id, EntityType t, Vector3 pos, Vector3 scl)
    {
        colliderID = id;
        type       = t;
        position   = pos;
        scale      = scl;
        isActive   = true;
    }
}

// ============================================================
// EntityManager
// Owns all entities except the player.
// Runs enemy patrol AI and fireball movement each frame.
// ============================================================
public class EntityManager : MonoBehaviour
{
    // ----------------------------------------------------------
    // Inspector settings
    // ----------------------------------------------------------
    [Header("Entity Settings")]
    public float enemySpeed          = 2.5f;
    public float fireballSpeed       = 22f;
    public float maxFireballDistance = 35f;
    public float entityDepth         = 0.5f; // Z thickness of all colliders

    // ----------------------------------------------------------
    // Private state
    // ----------------------------------------------------------
    private List<EntityData> _entities  = new List<EntityData>();
    private List<int>        _toRemove  = new List<int>(); // IDs queued for removal

    private GameManager _gameManager;

    // ----------------------------------------------------------
    // Unity lifecycle
    // ----------------------------------------------------------
    void Awake()
    {
        _gameManager = GetComponent<GameManager>();
    }

    // SpawnLevel is called by GameManager.Start() - no Start() here
    // to avoid ordering issues.

    void Update()
    {
        if (_gameManager.CurrentState != GameState.Playing) return;

        UpdateEnemies();
        UpdateFireballs();
        ProcessRemovals();
    }

    // ----------------------------------------------------------
    // Level layout - edit this to design your level
    // ----------------------------------------------------------
    public void SpawnLevel()
    {
        // ---- GROUND ----
        SpawnGround(new Vector3(20f, -5f, 0f), new Vector3(200f, 1f, 1f));

        // ---- PLATFORMS (regular obstacles) ----
        SpawnObstacle(new Vector3(5f,  -2f, 0f), new Vector3(5f, 1f, 1f), instakill: false);
        SpawnObstacle(new Vector3(13f,  0f, 0f), new Vector3(4f, 1f, 1f), instakill: false);
        SpawnObstacle(new Vector3(21f, -1f, 0f), new Vector3(6f, 1f, 1f), instakill: false);
        SpawnObstacle(new Vector3(30f,  1f, 0f), new Vector3(4f, 1f, 1f), instakill: false);
        SpawnObstacle(new Vector3(38f,  0f, 0f), new Vector3(5f, 1f, 1f), instakill: false);

        // ---- SPIKE TRAPS (instakill) ----
        SpawnObstacle(new Vector3(9f,  -4.5f, 0f), new Vector3(2f, 0.5f, 1f), instakill: true);
        SpawnObstacle(new Vector3(17f, -4.5f, 0f), new Vector3(2f, 0.5f, 1f), instakill: true);
        SpawnObstacle(new Vector3(26f, -4.5f, 0f), new Vector3(2f, 0.5f, 1f), instakill: true);

        // ---- ENEMIES ----
        SpawnEnemy(new Vector3(6f,  -1f, 0f),  3.0f,  7.0f);
        SpawnEnemy(new Vector3(14f,  1f, 0f), 11.5f, 14.5f);
        SpawnEnemy(new Vector3(22f,  0f, 0f), 18.5f, 23.5f);
        SpawnEnemy(new Vector3(32f,  2f, 0f), 28.5f, 31.5f);

        // ---- PICKUPS ----
        SpawnPickup(new Vector3(3f,  -3f, 0f), EntityType.FireballPickup);
        SpawnPickup(new Vector3(11f,  2f, 0f), EntityType.ExtraLifePickup);
        SpawnPickup(new Vector3(20f,  2f, 0f), EntityType.InvincibilityPickup);

        // ---- GOAL (end of level) ----
        SpawnGoal(new Vector3(60f, -3f, 0f));
    }

    // ----------------------------------------------------------
    // Per-frame AI updates
    // ----------------------------------------------------------
    void UpdateEnemies()
    {
        foreach (var e in _entities)
        {
            if (!e.isActive || e.type != EntityType.Enemy) continue;

            e.position.x += e.velocity.x * Time.deltaTime;

            // Bounce at patrol edges
            if (e.position.x >= e.patrolMaxX)
            {
                e.position.x = e.patrolMaxX;
                e.velocity.x = -enemySpeed;
            }
            else if (e.position.x <= e.patrolMinX)
            {
                e.position.x = e.patrolMinX;
                e.velocity.x =  enemySpeed;
            }

            SyncEntity(e);
        }
    }

    void UpdateFireballs()
    {
        foreach (var e in _entities)
        {
            if (!e.isActive || e.type != EntityType.Fireball) continue;

            float dx = e.velocity.x * Time.deltaTime;
            e.position.x         += dx;
            e.distanceTravelled  += Mathf.Abs(dx);

            // Remove if it has gone too far
            if (e.distanceTravelled >= e.maxDistance)
            {
                _toRemove.Add(e.colliderID);
                continue;
            }

            // Check what the fireball hits
            CollisionManager.Instance.CheckCollision(
                e.colliderID, e.position, out List<int> hitIds);

            bool destroyed = false;
            foreach (int hitId in hitIds)
            {
                EntityType hitType = CollisionManager.Instance.GetEntityType(hitId);

                if (hitType == EntityType.Enemy)
                {
                    _toRemove.Add(hitId);              // destroy enemy
                    _toRemove.Add(e.colliderID);       // destroy fireball
                    destroyed = true;
                    break;
                }
                if (hitType == EntityType.Obstacle   ||
                    hitType == EntityType.InstakillObstacle ||
                    hitType == EntityType.Ground)
                {
                    _toRemove.Add(e.colliderID);       // destroy fireball on walls
                    destroyed = true;
                    break;
                }
            }

            if (!destroyed) SyncEntity(e);
        }
    }

    // Apply position back to CollisionManager
    void SyncEntity(EntityData e)
    {
        Matrix4x4 m = Matrix4x4.TRS(e.position, Quaternion.identity, e.scale);
        CollisionManager.Instance.UpdateCollider(e.colliderID, e.position, GetColliderSize(e));
        CollisionManager.Instance.UpdateMatrix(e.colliderID, m);
    }

    Vector3 GetColliderSize(EntityData e)
    {
        return new Vector3(e.scale.x, e.scale.y, entityDepth);
    }

    // Remove all entities that were queued this frame
    void ProcessRemovals()
    {
        foreach (int id in _toRemove)
            RemoveEntityByColliderID(id);
        _toRemove.Clear();
    }

    // ----------------------------------------------------------
    // Spawn helpers
    // ----------------------------------------------------------
    public void SpawnGround(Vector3 pos, Vector3 scale)
    {
        Vector3 colSize = new Vector3(scale.x, scale.y, entityDepth);
        int id = CollisionManager.Instance.RegisterCollider(pos, colSize, false, EntityType.Ground);
        var e  = new EntityData(id, EntityType.Ground, pos, scale);
        CollisionManager.Instance.UpdateMatrix(id, Matrix4x4.TRS(pos, Quaternion.identity, scale));
        _entities.Add(e);
    }

    public void SpawnObstacle(Vector3 pos, Vector3 scale, bool instakill)
    {
        EntityType type   = instakill ? EntityType.InstakillObstacle : EntityType.Obstacle;
        Vector3    colSize = new Vector3(scale.x, scale.y, entityDepth);
        int id = CollisionManager.Instance.RegisterCollider(pos, colSize, false, type);
        var e  = new EntityData(id, type, pos, scale);
        CollisionManager.Instance.UpdateMatrix(id, Matrix4x4.TRS(pos, Quaternion.identity, scale));
        _entities.Add(e);
    }

    public void SpawnEnemy(Vector3 pos, float patrolMin, float patrolMax)
    {
        Vector3 scale   = Vector3.one;
        Vector3 colSize = new Vector3(scale.x, scale.y, entityDepth);
        int id = CollisionManager.Instance.RegisterCollider(pos, colSize, false, EntityType.Enemy);
        var e  = new EntityData(id, EntityType.Enemy, pos, scale);
        e.patrolMinX = patrolMin;
        e.patrolMaxX = patrolMax;
        e.velocity   = new Vector3(enemySpeed, 0f, 0f);
        CollisionManager.Instance.UpdateMatrix(id, Matrix4x4.TRS(pos, Quaternion.identity, scale));
        _entities.Add(e);
    }

    public void SpawnPickup(Vector3 pos, EntityType pickupType)
    {
        Vector3 scale   = new Vector3(0.7f, 0.7f, 0.7f);
        Vector3 colSize = new Vector3(scale.x, scale.y, entityDepth);
        int id = CollisionManager.Instance.RegisterCollider(pos, colSize, false, pickupType);
        var e  = new EntityData(id, pickupType, pos, scale);
        CollisionManager.Instance.UpdateMatrix(id, Matrix4x4.TRS(pos, Quaternion.identity, scale));
        _entities.Add(e);
    }

    public void SpawnGoal(Vector3 pos)
    {
        Vector3 scale   = new Vector3(2f, 3f, 1f);
        Vector3 colSize = new Vector3(scale.x, scale.y, entityDepth);
        int id = CollisionManager.Instance.RegisterCollider(pos, colSize, false, EntityType.Goal);
        var e  = new EntityData(id, EntityType.Goal, pos, scale);
        CollisionManager.Instance.UpdateMatrix(id, Matrix4x4.TRS(pos, Quaternion.identity, scale));
        _entities.Add(e);
    }

    public void SpawnFireball(Vector3 pos, float directionX)
    {
        Vector3 scale   = new Vector3(0.5f, 0.3f, 0.3f);
        Vector3 colSize = new Vector3(scale.x, scale.y, entityDepth);
        int id = CollisionManager.Instance.RegisterCollider(pos, colSize, false, EntityType.Fireball);
        var e  = new EntityData(id, EntityType.Fireball, pos, scale);
        e.velocity    = new Vector3(fireballSpeed * directionX, 0f, 0f);
        e.maxDistance = maxFireballDistance;
        CollisionManager.Instance.UpdateMatrix(id, Matrix4x4.TRS(pos, Quaternion.identity, scale));
        _entities.Add(e);
    }

    // ----------------------------------------------------------
    // Removal (called by PlayerController and fireball logic)
    // ----------------------------------------------------------
    public void RemoveEntityByColliderID(int colliderID)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            if (_entities[i].colliderID == colliderID)
            {
                _entities[i].isActive = false;
                CollisionManager.Instance.RemoveCollider(colliderID);
                _entities.RemoveAt(i);
                return;
            }
        }
    }

    // Called by GameRenderer every frame to build draw calls
    public List<EntityData> GetActiveEntities()
    {
        return _entities;
    }
}