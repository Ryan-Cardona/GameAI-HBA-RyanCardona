using UnityEngine;
using UnityEngine.AI;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

[RequireComponent(typeof(NavMeshAgent))]
public class GladiatorAgent : Agent
{
    [Header("References")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private VisionSensor vision;
    [SerializeField] private Weapon weapon;
    [SerializeField] private Health health;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float turnSpeed = 180f;

    [Header("Combat")]
    [SerializeField] private float attackRange = 12f;

    private Transform currentEnemy;

    public override void Initialize()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (vision == null) vision = GetComponent<VisionSensor>();
        if (weapon == null) weapon = GetComponent<Weapon>();
        if (health == null) health = GetComponent<Health>();

        agent.speed = moveSpeed;
        agent.angularSpeed = turnSpeed;
    }

    public override void OnEpisodeBegin()
    {
        // Reset agent
        agent.ResetPath();
        health.ResetHealth();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Health %
        sensor.AddObservation(health.CurrentHealth / health.MaxHealth);

        // Ammo %
        sensor.AddObservation((float)weapon.Ammo / weapon.MaxAmmo);

        // Can we see an enemy?
        bool seesEnemy = vision.ClosestVisibleTarget != null;
        sensor.AddObservation(seesEnemy ? 1f : 0f);

        if (seesEnemy)
        {
            Vector3 toEnemy = vision.ClosestVisibleTarget.position - transform.position;
            sensor.AddObservation(toEnemy.normalized);
            sensor.AddObservation(toEnemy.magnitude / 20f); // normalized distance
        }
        else
        {
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(0f);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        
        // Continuous actions:
        // 0 = move forward/back
        // 1 = turn left/right
        // 2 = shoot (0 or 1)

        float move = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float turn = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float shoot = actions.ContinuousActions[2];

        // Move
        Vector3 moveDir = transform.forward * move;
        agent.Move(moveDir * moveSpeed * Time.deltaTime);

        // Turn
        transform.Rotate(Vector3.up, turn * turnSpeed * Time.deltaTime);

        // Shoot
        if (shoot > 0.5f)
        {
            weapon.TryFire();
        }

        // Small living penalty (encourages efficiency)
        AddReward(-0.001f);

        // Reward for seeing enemy (encourages searching)
        if (vision.ClosestVisibleTarget != null)
        {
            AddReward(0.001f);
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // For testing: WASD + Space
        var ca = actionsOut.ContinuousActions;

        ca[0] = Input.GetAxis("Vertical");    // W/S
        ca[1] = Input.GetAxis("Horizontal");  // A/D
        ca[2] = Input.GetKey(KeyCode.Space) ? 1f : 0f;
    }

    // -------------------- REWARD HOOKS --------------------

    public void RewardHitEnemy()
    {
        AddReward(0.5f);
    }

    public void RewardKillEnemy()
    {
        AddReward(2.0f);
        EndEpisode();
    }

    public void PunishDeath()
    {
        AddReward(-2.0f);
        EndEpisode();
    }
}
