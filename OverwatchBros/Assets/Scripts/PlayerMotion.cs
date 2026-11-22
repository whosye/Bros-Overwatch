using UnityEngine;

// Source-style friction followed by acceleration in the requested direction.
// There is no fast/slow branch based on the sign of residual velocity.
// Knockback, launch and traversal keep their own motion.
public static class PlayerMotion
{
    public static Vector3 Step(Vector3 velocity, Vector3 target, float topSpeed, float turnSeconds, float dt)
    {
        if (dt <= 0f) return velocity;
        if (topSpeed <= 0f) return Vector3.zero;
        velocity.y = 0f;
        target.y = 0f;
        velocity = Vector3.ClampMagnitude(velocity, topSpeed);
        target = Vector3.ClampMagnitude(target, topSpeed);
        float wishSpeed = target.magnitude;
        Vector3 wishDirection = wishSpeed > .0001f ? target / wishSpeed : Vector3.zero;
        float acceleration = 1f / Mathf.Max(.01f,turnSeconds);
        const float friction = 6f;
        float stopSpeed = topSpeed / 3f;
        // Small steps keep friction/counter-strafing consistent at different render rates.
        while (dt > .000001f)
        {
            float step = Mathf.Min(dt,1f / 240f);
            dt -= step;
            float speed = velocity.magnitude;
            if (speed > .00001f)
            {
                float drop = Mathf.Max(speed,stopSpeed) * friction * step;
                velocity *= Mathf.Max(0f,speed - drop) / speed;
            }
            if (wishSpeed > .0001f)
            {
                float addSpeed = wishSpeed - Vector3.Dot(velocity,wishDirection);
                if (addSpeed > 0f)
                    velocity += wishDirection * Mathf.Min(addSpeed,acceleration * wishSpeed * step);
            }
            // Do not permit diagonal changes or repeated taps to build extra speed.
            velocity = Vector3.ClampMagnitude(velocity,topSpeed);
        }
        return velocity;
    }

    public static Vector3 AfterCollision(Vector3 requested,Vector3 travelled,float dt) => dt > 0f
        ? Vector3.ClampMagnitude(new Vector3(travelled.x,0,travelled.z) / dt,requested.magnitude) : Vector3.zero;
}
