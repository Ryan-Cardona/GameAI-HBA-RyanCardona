using UnityEngine;
using UnityEngine.AI;

public class GuardAI_FSM : MonoBehaviour
{
    private enum State
    {
        Patrol,
        Chase,
        Search,
        Attack
    }

    [Header("References")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private VisionSensor vision;
    [SerializeField] private Weapon weapon;
    [SerializeField] private Health health;

    [Header("Patrol")]
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private float patrolPointReachDistance = 1.2f;
    [SerializeField] private float waitAtPointTime = 1.0f;

    [Header("Chase")]
    [SerializeField] private float chaseRepathInterval = 0.25f;

    [Header("Attack")]
    [SerializeField] private float attackRange = 12f; // should be <= weapon range
    [SerializeField] private float faceTargetTurnSpeed = 360f;

    [Header("Search")]
    [SerializeField] private float searchDuration = 3.0f;
    [SerializeField] private float searchWanderRadius = 4.0f;

    [Header("Debug")]
    [SerializeField] private bool logStateChanges = false;

    private State state;
    private int patrolIndex;

    private Transform currentTarget;          // what we are pursuing
    private Vector3 lastKnownTargetPos;       // used for search

    private float waitTimer;
    private float nextRepathTime;
    private float searchTimer;

    private void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (vision == null) vision = GetComponent<VisionSensor>();
        if (weapon == null) weapon = GetComponent<Weapon>();
        if (health == null) health = GetComponent<Health>();

        if (agent == null) Debug.LogError($"{name}: GuardAI_FSM needs a NavMeshAgent.");
        if (vision == null) Debug.LogError($"{name}: GuardAI_FSM needs a VisionSensor.");
        if (weapon == null) Debug.LogError($"{name}: GuardAI_FSM needs a Weapon.");
        if (health == null) Debug.LogError($"{name}: GuardAI_FSM needs a Health.");

        SetState(State.Patrol);
    }

    private void Update()
    {
        if (health != null && health.IsDead) return;

        // Always keep an eye out
        Transform seen = (vision != null) ? vision.ClosestVisibleTarget : null;
        if (seen != null)
        {
            currentTarget = seen;
            lastKnownTargetPos = currentTarget.position;

            // If we can attack, do it; otherwise chase
            if (IsInAttackRange(currentTarget))
                SetState(State.Attack);
            else
                SetState(State.Chase);
        }

        switch (state)
        {
            case State.Patrol: TickPatrol(); break;
            case State.Chase:  TickChase();  break;
            case State.Attack: TickAttack(); break;
            case State.Search: TickSearch(); break;
        }
    }

    // -------------------- STATES --------------------

    private void TickPatrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
            return;

        Transform point = patrolPoints[patrolIndex];

        // If we don't currently have a path, set one
        if (!agent.pathPending && (agent.pathStatus != NavMeshPathStatus.PathInvalid) && !agent.hasPath)
        {
            agent.SetDestination(point.position);
        }

        // Wait until a path is computed
        if (agent.pathPending) return;

        // "Arrived" check using agent's navigation values
        if (agent.remainingDistance <= patrolPointReachDistance)
        {
            // Make sure we are actually stopped (not still decelerating)
            if (agent.hasPath && agent.velocity.sqrMagnitude > 0.01f)
                return;

            waitTimer += Time.deltaTime;

            if (waitTimer >= waitAtPointTime)
            {
                waitTimer = 0f;
                patrolIndex = (patrolIndex + 1) % patrolPoints.Length;

                agent.SetDestination(patrolPoints[patrolIndex].position);
            }
        }
        else
        {
            // If we moved away from the point again, reset wait
            waitTimer = 0f;
        }
    }


    private void TickChase()
    {
        if (currentTarget == null || !currentTarget.gameObject.activeInHierarchy)
        {
            // Lost target -> search last known position
            SetState(State.Search);
            return;
        }

        lastKnownTargetPos = currentTarget.position;

        if (Time.time >= nextRepathTime)
        {
            nextRepathTime = Time.time + chaseRepathInterval;
            agent.SetDestination(lastKnownTargetPos);
        }

        if (IsInAttackRange(currentTarget))
            SetState(State.Attack);
    }

    private void TickAttack()
    {
        if (currentTarget == null || !currentTarget.gameObject.activeInHierarchy)
        {
            SetState(State.Search);
            return;
        }

        // If we can still see them, keep updating last known
        lastKnownTargetPos = currentTarget.position;

        float dist = Vector3.Distance(transform.position, currentTarget.position);
        if (dist > attackRange)
        {
            SetState(State.Chase);
            return;
        }

        // Stop moving while shooting
        agent.ResetPath();

        FaceTarget(currentTarget);

        // Fire whenever cooldown allows
        weapon.TryFire();
    }

    private void TickSearch()
    {
        searchTimer += Time.deltaTime;

        // Move toward last known position first
        if (!agent.hasPath)
        {
            agent.SetDestination(lastKnownTargetPos);
        }

        // Once near last known position, wander a bit
        float distToLastKnown = Vector3.Distance(transform.position, lastKnownTargetPos);
        if (distToLastKnown <= 1.5f && !agent.pathPending && agent.remainingDistance <= 1.5f)
        {
            Vector3 wander = GetRandomNavmeshPoint(transform.position, searchWanderRadius);
            agent.SetDestination(wander);
        }

        // If search time expires, return to patrol
        if (searchTimer >= searchDuration)
        {
            currentTarget = null;
            SetState(State.Patrol);
        }
    }

    // -------------------- HELPERS --------------------

    private bool IsInAttackRange(Transform t)
    {
        if (t == null) return false;
        return Vector3.Distance(transform.position, t.position) <= attackRange;
    }

    private void FaceTarget(Transform t)
    {
        Vector3 toTarget = (t.position - transform.position);
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, faceTargetTurnSpeed * Time.deltaTime);
    }

    private Vector3 GetRandomNavmeshPoint(Vector3 center, float radius)
    {
        for (int i = 0; i < 20; i++)
        {
            Vector3 random = center + Random.insideUnitSphere * radius;
            random.y = center.y;

            if (NavMesh.SamplePosition(random, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
                return hit.position;
        }

        return center; // fallback
    }

    private void SetState(State newState)
    {
        if (state == newState) return;

        state = newState;

        if (logStateChanges)
            Debug.Log($"{name} -> {state}");

        // Enter-state resets
        switch (state)
        {
            case State.Patrol:
                searchTimer = 0f;
                waitTimer = 0f;
                break;

            case State.Chase:
                nextRepathTime = 0f;
                break;

            case State.Attack:
                break;

            case State.Search:
                searchTimer = 0f;
                agent.ResetPath();
                agent.SetDestination(lastKnownTargetPos);
                break;
        }
    }
}
