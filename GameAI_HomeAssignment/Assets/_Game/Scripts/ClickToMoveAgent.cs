using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class ClickToMoveAgent : MonoBehaviour
{
    private NavMeshAgent agent;
    private Camera mainCam;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        mainCam = Camera.main;
    }

    private void Update()
    {
        if (mainCam == null) return;

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                // Ensure the clicked point is on (or near) the NavMesh.
                if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 2.0f, NavMesh.AllAreas))
                {
                    agent.SetDestination(navHit.position);
                }
            }
        }
    }
}