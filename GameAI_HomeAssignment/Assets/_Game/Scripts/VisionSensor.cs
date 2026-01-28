using System.Collections.Generic;
using UnityEngine;

public class VisionSensor : MonoBehaviour
{
    [Header("Vision Settings")]
    [SerializeField] private Transform eyes;
    [SerializeField] private float viewDistance = 15f;
    [SerializeField, Range(0f, 360f)] private float viewAngle = 120f;

    [Header("Detection Layers")]
    [Tooltip("Which layers contain agents?")]
    [SerializeField] private LayerMask agentLayers;
    [Tooltip("Which layers block vision?")]
    [SerializeField] private LayerMask obstructionLayers;

    [Header("Performance")]
    [Tooltip("How often to refresh vision checks (seconds). Lower = more responsive, higher = faster.")]
    [SerializeField] private float refreshInterval = 0.15f;

    private readonly List<Transform> visibleTargets = new List<Transform>();
    public IReadOnlyList<Transform> VisibleTargets => visibleTargets;

    public Transform ClosestVisibleTarget { get; private set; }

    private float nextRefreshTime;

    private void Awake()
    {
        if (eyes == null)
        {
            // Try to find a child called "Eyes"
            Transform found = transform.Find("Eyes");
            if (found != null) eyes = found;
        }

        if (eyes == null)
            Debug.LogError($"VisionSensor on {name} needs an Eyes transform assigned.");

        // If masks are not set, default to Everything
        if (agentLayers.value == 0) agentLayers = ~0;
        if (obstructionLayers.value == 0) obstructionLayers = ~0;
    }

    private void Update()
    {
        if (Time.time >= nextRefreshTime)
        {
            nextRefreshTime = Time.time + refreshInterval;
            RefreshVision();
        }
    }

    private void RefreshVision()
    {
        visibleTargets.Clear();
        ClosestVisibleTarget = null;

        if (eyes == null) return;

        Collider[] hits = Physics.OverlapSphere(eyes.position, viewDistance, agentLayers, QueryTriggerInteraction.Ignore);

        float closestSqr = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            Transform candidate = hits[i].transform;

            // Ignore self (root compare)
            if (candidate.root == transform.root)
                continue;

            // Ignore dead targets (if they have Health and are dead/inactive)
            Health h = candidate.GetComponentInParent<Health>();
            if (h != null && h.IsDead) continue;
            if (!candidate.gameObject.activeInHierarchy) continue;

            Vector3 toTarget = (candidate.position - eyes.position);
            float distance = toTarget.magnitude;

            // Angle check
            Vector3 dir = toTarget.normalized;
            float angleToTarget = Vector3.Angle(eyes.forward, dir);
            if (angleToTarget > viewAngle * 0.5f)
                continue;

            // Line-of-sight check
            // Raycast from eyes toward target. If something blocks it, can't see.
            if (Physics.Raycast(eyes.position, dir, out RaycastHit hit, distance, obstructionLayers, QueryTriggerInteraction.Ignore))
            {
                // If we hit something before reaching the target, it is obstructed.
                // BUT if the hit is actually the target collider, it's fine.
                if (hit.transform.root != candidate.root)
                    continue;
            }

            // Passed all checks => visible
            visibleTargets.Add(candidate);

            float sqr = toTarget.sqrMagnitude;
            if (sqr < closestSqr)
            {
                closestSqr = sqr;
                ClosestVisibleTarget = candidate;
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (eyes == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(eyes.position, viewDistance);

        // Draw FOV lines (approx)
        Vector3 leftDir = Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * eyes.forward;
        Vector3 rightDir = Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * eyes.forward;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(eyes.position, eyes.position + leftDir * viewDistance);
        Gizmos.DrawLine(eyes.position, eyes.position + rightDir * viewDistance);

        if (ClosestVisibleTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(eyes.position, ClosestVisibleTarget.position);
        }
    }
#endif
}
