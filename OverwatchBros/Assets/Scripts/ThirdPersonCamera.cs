using Unity.Netcode;
using UnityEngine;

// Kamera za zady hrace, ale nikdy ne za zdi/stropem (uvnitr budovy se privede blize k hraci).
public static class ThirdPersonCamera
{
    public static void Place(Transform player, NetworkObject self, Camera camera, Vector3 headLocal, Vector3 offset)
    {
        Vector3 head = player.TransformPoint(headLocal);
        Vector3 desired = player.TransformPoint(offset);
        Vector3 direction = desired - head;
        float distance = direction.magnitude;
        if (distance < 0.01f) return;
        direction /= distance;

        float allowed = distance;
        var hits = Physics.SphereCastAll(head, 0.25f, direction, distance, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && owner == self) continue;
            if (hit.distance <= 0f) continue;

            allowed = Mathf.Min(allowed, hit.distance);
        }

        camera.transform.position = head + direction * Mathf.Max(0.15f, allowed - 0.05f);
    }

    // Kamera obihajici kolem hlavy podle smeru pohledu (pri pohledu dolu je nad hracem a vidi ho i zem pod nim).
    public static void PlaceOrbit(Transform player, NetworkObject self, Camera camera, Vector3 headLocal, float distance, float lift)
    {
        Vector3 head = player.TransformPoint(headLocal);
        Vector3 direction = (-camera.transform.forward * distance + Vector3.up * lift).normalized;
        float length = Mathf.Sqrt(distance * distance + lift * lift);

        float allowed = length;
        foreach (var hit in Physics.SphereCastAll(head, 0.25f, direction, length, ~0, QueryTriggerInteraction.Ignore))
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && owner == self) continue;
            if (hit.distance <= 0f) continue;
            allowed = Mathf.Min(allowed, hit.distance);
        }

        camera.transform.position = head + direction * Mathf.Max(0.15f, allowed - 0.05f);
    }
}
