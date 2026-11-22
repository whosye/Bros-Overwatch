using UnityEngine;

// Mouse delta is already displacement, not a velocity. Keep legacy sensitivity at 60 FPS.
public sealed class MouseLookInput
{
    bool active;
    int ignoreThrough = -1;

    public void Reset(int frame) { active = false; ignoreThrough = frame + 1; }

    public bool TryRead(Vector2 delta, bool locked, bool focused, int frame, float frameSeconds, out Vector2 result)
    {
        result = Vector2.zero;
        if (!locked || !focused) { active = false; return false; }
        // Locking/centering the OS cursor and regaining focus can generate a synthetic delta.
        if (!active) { active = true; ignoreThrough = Mathf.Max(ignoreThrough,frame); }
        if (frame <= ignoreThrough || frameSeconds > .25f) return false;
        if (float.IsNaN(delta.x) || float.IsNaN(delta.y) || float.IsInfinity(delta.x) || float.IsInfinity(delta.y)) return false;
        result = delta;
        return true;
    }

    public static Vector2 Degrees(Vector2 delta,float sensitivity) => delta * (sensitivity / 60f);
}
