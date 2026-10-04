using UnityEngine;

// Solid to players at all times. Only the assigned traffic car ignores these colliders.
public sealed class TrafficBoundary : MonoBehaviour
{
    public YarisTraffic car;

    void OnEnable() => ApplyCollisionExceptions();

    public void ApplyCollisionExceptions()
    {
        if (car == null) return;
        foreach (var wall in GetComponentsInChildren<Collider>(true))
            foreach (var vehicle in car.GetComponentsInChildren<Collider>(true))
                Physics.IgnoreCollision(wall, vehicle, true);
    }
}
