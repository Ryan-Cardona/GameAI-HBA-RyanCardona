using UnityEngine;
using UnityEngine.AI;

public class StrategistAI_Utility : MonoBehaviour
{
    private enum ActionState
    {
        None,
        Fight,
        Chase,
        GetHealth,
        GetAmmo,
        Flee,
        Wander
    }

    [Header("References")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private VisionSensor vision;
    [SerializeField] private Weapon weapon;
    [SerializeField] private Health health;

    [Header("Decision Timing")]
    [SerializeField] private float decisionInterval = 0.25f;

    [Header("Stability (Prevents Thrashing)")]
    [Tooltip("Minimum seconds to stick with an action before switching (except critical survival override).")]
    [SerializeField] private float minActionHoldTime = 1.2f;

    [Tooltip("How much better a new action's score must be to switch from the current action.")]
    [SerializeField] private float switchScoreThreshold = 8f;

    [Header("Enemy Memory")]
    [Tooltip("How long to remember last seen enemy position after losing sight.")]
    [SerializeField] private float enemyMemoryTime = 3.0f;

    [Header("Survival Override")]
    [SerializeField] private float criticalHealthPercent = 0.3f; // 30%

    [Header("Ranges")]
    [SerializeField] private float attackRange = 12f;
    [SerializeField] private float fleeDistance = 10f;

    [Header("Wander")]
    [SerializeField] private float wanderRadius = 18f;
    [SerializeField] private float minWanderDistance = 6f;
    [SerializeField] private float wanderRepathTime = 6.0f;
    [SerializeField] private float wanderArriveDistance = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool logDecisions = false;

    private float nextDecisionTime;
    private float nextWanderTime;

    private ActionState currentAction = ActionState.None;
    private float currentActionScore = float.NegativeInfinity;
    private float currentActionStartTime;

    // Enemy tracking
    private Transform currentEnemy;
    private Vector3 lastKnownEnemyPos;
    private float lastSeenEnemyTime;

    private void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (vision == null) vision = GetComponent<VisionSensor>();
        if (weapon == null) weapon = GetComponent<Weapon>();
        if (health == null) health = GetComponent<Health>();

        if (agent == null) Debug.LogError($"{name}: Strategist needs NavMeshAgent");
        if (vision == null) Debug.LogError($"{name}: Strategist needs VisionSensor");
        if (weapon == null) Debug.LogError($"{name}: Strategist needs Weapon");
        if (health == null) Debug.LogError($"{name}: Strategist needs Health");

        // Start with wander so it doesn't stand still
        ForceSetAction(ActionState.Wander, 5f, "Init -> Wander");
        TryPickNewWanderDestination();
    }

    private void Update()
    {
        if (health != null && health.IsDead) return;

        // Update enemy memory if we currently see someone
        if (vision != null && vision.ClosestVisibleTarget != null)
        {
            currentEnemy = vision.ClosestVisibleTarget;
            lastKnownEnemyPos = currentEnemy.position;
            lastSeenEnemyTime = Time.time;
        }

        if (Time.time >= nextDecisionTime)
        {
            nextDecisionTime = Time.time + decisionInterval;
            MakeDecision();
        }

        TickCurrentAction();
    }

    // -------------------- DECISION --------------------

    private void MakeDecision()
    {
        float healthPercent = health.CurrentHealth / health.MaxHealth;

        // ===== SURVIVAL OVERRIDE (Hierarchical Goal) =====
        // Critical survival overrides action stability rules.
        if (healthPercent <= criticalHealthPercent)
        {
            ResourcePickup hp = FindClosestActiveResource(ResourceType.Health);
            if (hp != null)
            {
                ForceSetAction(ActionState.GetHealth, 999f, "CRITICAL -> GetHealth");
                agent.SetDestination(hp.transform.position);
                return;
            }

            ForceSetAction(ActionState.Flee, 999f, "CRITICAL -> Flee");
            FleeFromEnemyOrLastKnown();
            return;
        }

        // Score all actions
        float fight = ScoreFight();
        float chase = ScoreChase();
        float getHealth = ScoreGetHealth();
        float getAmmo = ScoreGetAmmo();
        float flee = ScoreFlee();
        float wander = ScoreWander();

        // Pick best
        ActionState bestAction = ActionState.Wander;
        float bestScore = wander;

        PickIfBetter(ref bestAction, ref bestScore, ActionState.Fight, fight);
        PickIfBetter(ref bestAction, ref bestScore, ActionState.Chase, chase);
        PickIfBetter(ref bestAction, ref bestScore, ActionState.GetHealth, getHealth);
        PickIfBetter(ref bestAction, ref bestScore, ActionState.GetAmmo, getAmmo);
        PickIfBetter(ref bestAction, ref bestScore, ActionState.Flee, flee);

        // Decide whether we are allowed to switch (prevents thrashing)
        bool holdingAction = (Time.time - currentActionStartTime) < minActionHoldTime;
        bool shouldSwitch =
            currentAction == ActionState.None ||
            bestAction == currentAction ||
            (!holdingAction && bestScore >= currentActionScore + switchScoreThreshold);

        // NEW: If we're doing a movement action, commit to the destination until we arrive,
        // unless the best action is Fight (so it can stop and shoot immediately).
        if (IsMoveAction(currentAction))
        {
            if (!HasArrived(wanderArriveDistance))
            {
                bool canInstantFight = (bestAction == ActionState.Fight);

                if (!canInstantFight)
                    shouldSwitch = false;
            }
        }

        // Also: if our current action has become "invalid", allow switching immediately
        // BUT: for move actions, only bypass the arrival lock if we've arrived.
        if (CurrentActionInvalid())
        {
            if (!IsMoveAction(currentAction) || HasArrived(wanderArriveDistance))
                shouldSwitch = true;
        }

        if (!shouldSwitch)
        {
            // Keep doing what we're doing
            return;
        }

        SetAction(bestAction, bestScore, $"Utility -> {bestAction} ({bestScore:0.0})");
        ExecuteAction(bestAction);
    }

    private void PickIfBetter(ref ActionState bestAction, ref float bestScore, ActionState candidateAction, float candidateScore)
    {
        if (candidateScore > bestScore)
        {
            bestScore = candidateScore;
            bestAction = candidateAction;
        }
    }

    // -------------------- SCORING --------------------

    private float ScoreFight()
    {
        if (!HasVisibleEnemy()) return -50f;
        if (weapon.Ammo <= 0) return -50f;

        float dist = Vector3.Distance(transform.position, currentEnemy.position);
        if (dist > attackRange) return -20f;

        float healthPercent = health.CurrentHealth / health.MaxHealth;
        return 60f + healthPercent * 20f; // 60..80
    }

    private float ScoreChase()
    {
        if (!HasRecentEnemy()) return -40f;
        if (weapon.Ammo <= 0) return -20f;

        float dist = Vector3.Distance(transform.position, lastKnownEnemyPos);
        if (dist <= attackRange) return -10f;

        return 45f;
    }

    private float ScoreGetHealth()
    {
        float healthPercent = health.CurrentHealth / health.MaxHealth;
        if (healthPercent >= 0.95f) return -30f;

        ResourcePickup hp = FindClosestActiveResource(ResourceType.Health);
        if (hp == null) return -40f;

        float dist = Vector3.Distance(transform.position, hp.transform.position);

        float urgency = (1f - healthPercent) * 90f;   // 0..90
        float distancePenalty = Mathf.Clamp(dist, 0f, 30f);

        return urgency - distancePenalty;
    }

    private float ScoreGetAmmo()
    {
        float ammoPercent = (weapon.MaxAmmo <= 0) ? 0f : (weapon.Ammo / (float)weapon.MaxAmmo);
        if (ammoPercent >= 0.9f) return -30f;

        ResourcePickup ammo = FindClosestActiveResource(ResourceType.Ammo);
        if (ammo == null) return -40f;

        float dist = Vector3.Distance(transform.position, ammo.transform.position);

        float urgency = (1f - ammoPercent) * 80f; // 0..80
        float distancePenalty = Mathf.Clamp(dist, 0f, 30f);

        return urgency - distancePenalty;
    }

    private float ScoreFlee()
    {
        if (!HasRecentEnemy()) return -50f;

        float healthPercent = health.CurrentHealth / health.MaxHealth;
        if (healthPercent > 0.6f) return -30f;

        return 40f + (1f - healthPercent) * 60f; // 40..100
    }

    private float ScoreWander()
    {
        // Always available, low priority
        return 10f;
    }

    // -------------------- EXECUTION --------------------

    private void ExecuteAction(ActionState action)
    {
        switch (action)
        {
            case ActionState.Fight:
                agent.ResetPath();
                break;

            case ActionState.Chase:
                if (HasRecentEnemy())
                    agent.SetDestination(lastKnownEnemyPos);
                break;

            case ActionState.GetHealth:
                {
                    ResourcePickup hp = FindClosestActiveResource(ResourceType.Health);
                    if (hp != null) agent.SetDestination(hp.transform.position);
                }
                break;

            case ActionState.GetAmmo:
                {
                    ResourcePickup ammo = FindClosestActiveResource(ResourceType.Ammo);
                    if (ammo != null) agent.SetDestination(ammo.transform.position);
                }
                break;

            case ActionState.Flee:
                FleeFromEnemyOrLastKnown();
                break;

            case ActionState.Wander:
                TryPickNewWanderDestination();
                break;
        }
    }

    // -------------------- TICK CURRENT ACTION --------------------

    private void TickCurrentAction()
    {
        // Fight: face + shoot when in range
        if (currentAction == ActionState.Fight && HasVisibleEnemy())
        {
            float dist = Vector3.Distance(transform.position, currentEnemy.position);
            if (dist <= attackRange)
            {
                FaceTarget(currentEnemy);
                weapon.TryFire();
            }
        }

        // Chase: keep destination fresh while enemy is remembered
        if (currentAction == ActionState.Chase && HasRecentEnemy())
        {
            if (!agent.pathPending && agent.remainingDistance <= 1.5f)
                agent.SetDestination(lastKnownEnemyPos);
        }

        // Wander: periodically choose a new point
        if (currentAction == ActionState.Wander)
        {
            // Only pick a new wander destination when we actually arrive (or after a long timeout)
            if (!agent.pathPending && agent.hasPath && agent.remainingDistance <= wanderArriveDistance)
            {
                TryPickNewWanderDestination();
            }
            else if (Time.time >= nextWanderTime && (!agent.pathPending))
            {
                // Timeout safety: if path got stuck or took too long, pick a new one
                TryPickNewWanderDestination();
            }
        }
    }

    // -------------------- VALIDITY --------------------

    private bool CurrentActionInvalid()
    {
        // If we're fighting but no enemy / no ammo, invalid
        if (currentAction == ActionState.Fight)
        {
            if (!HasVisibleEnemy()) return true;
            if (weapon.Ammo <= 0) return true;
        }

        // If we're chasing but we no longer remember any enemy, invalid
        if (currentAction == ActionState.Chase)
        {
            if (!HasRecentEnemy()) return true;
        }

        // If we are going to ammo but we are basically full, invalid
        if (currentAction == ActionState.GetAmmo)
        {
            float ammoPercent = (weapon.MaxAmmo <= 0) ? 0f : (weapon.Ammo / (float)weapon.MaxAmmo);
            if (ammoPercent >= 0.95f) return true;
        }

        // If we are going to health but we're basically full, invalid
        if (currentAction == ActionState.GetHealth)
        {
            float healthPercent = health.CurrentHealth / health.MaxHealth;
            if (healthPercent >= 0.98f) return true;
        }

        return false;
    }

    // -------------------- HELPERS --------------------

    private bool HasVisibleEnemy()
    {
        return vision != null && vision.ClosestVisibleTarget != null && vision.ClosestVisibleTarget.gameObject.activeInHierarchy;
    }

    private bool HasRecentEnemy()
    {
        if (HasVisibleEnemy()) return true;
        return (Time.time - lastSeenEnemyTime) <= enemyMemoryTime;
    }

    private void FaceTarget(Transform t)
    {
        Vector3 toTarget = (t.position - transform.position);
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, 360f * Time.deltaTime);
    }

    private void FleeFromEnemyOrLastKnown()
    {
        Vector3 threatPos = HasVisibleEnemy() ? vision.ClosestVisibleTarget.position : lastKnownEnemyPos;

        Vector3 away = (transform.position - threatPos).normalized;
        if (away.sqrMagnitude < 0.001f) away = transform.forward;

        Vector3 targetPos = transform.position + away * fleeDistance;

        if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    private void TryPickNewWanderDestination()
    {
        nextWanderTime = Time.time + wanderRepathTime;

        // Try multiple times to find a point that is not too close
        for (int i = 0; i < 25; i++)
        {
            Vector3 candidate = transform.position + Random.insideUnitSphere * wanderRadius;
            candidate.y = transform.position.y;

            // Sample with a slightly larger max distance for more stable results
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 6f, NavMesh.AllAreas))
            {
                float dist = Vector3.Distance(transform.position, hit.position);
                if (dist >= minWanderDistance)
                {
                    agent.SetDestination(hit.position);
                    return;
                }
            }
        }

        // Fallback: if we couldn't find a far point, just pick *any* valid nearby one.
        Vector3 fallback = transform.position + Random.insideUnitSphere * wanderRadius;
        fallback.y = transform.position.y;

        if (NavMesh.SamplePosition(fallback, out NavMeshHit fallbackHit, 6f, NavMesh.AllAreas))
            agent.SetDestination(fallbackHit.position);
    }

    private ResourcePickup FindClosestActiveResource(ResourceType type)
    {
        ResourcePickup[] all = GameObject.FindObjectsOfType<ResourcePickup>();

        ResourcePickup best = null;
        float bestDist = float.PositiveInfinity;

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            if (!all[i].gameObject.activeInHierarchy) continue;

            Collider c = all[i].GetComponent<Collider>();
            if (c == null || !c.enabled) continue;

            if (all[i].GetResourceType() != type) continue;

            float d = Vector3.Distance(transform.position, all[i].transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = all[i];
            }
        }

        return best;
    }

    private void SetAction(ActionState action, float score, string reason)
    {
        if (currentAction == action) return;

        currentAction = action;
        currentActionScore = score;
        currentActionStartTime = Time.time;

        if (logDecisions)
            Debug.Log($"{name}: {reason} | hold {minActionHoldTime}s | switch+{switchScoreThreshold}");
    }

    private void ForceSetAction(ActionState action, float score, string reason)
    {
        currentAction = action;
        currentActionScore = score;
        currentActionStartTime = Time.time;

        if (logDecisions)
            Debug.Log($"{name}: {reason}");
    }

    // -------------------- NEW HELPERS (Arrival Lock) --------------------

    private bool HasArrived(float stopDistance)
    {
        if (agent.pathPending) return false;
        if (!agent.hasPath) return true;
        if (agent.remainingDistance > stopDistance) return false;

        return agent.velocity.sqrMagnitude < 0.01f;
    }

    private bool IsMoveAction(ActionState a)
    {
        return a == ActionState.Wander ||
               a == ActionState.GetAmmo ||
               a == ActionState.GetHealth ||
               a == ActionState.Chase ||
               a == ActionState.Flee;
    }
}
