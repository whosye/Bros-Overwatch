using UnityEngine;

// Houpani pupku (Honza): kost "Pupek" (dite Spine, ma vahy na brise) se v LateUpdate posune podle zrychleni
// trupu - pruzina s tlumenim. Pri behu, skoku a dopadu se pupek opozdi za telem a dokmita. Animace kost
// nehybe, proto se poloha nastavuje vzdy znovu (klid + odchylka). Pridava ji AyranAvatarSetup do prefabu postavy,
// ktera kost Pupek ma.
public class BellyJiggle : MonoBehaviour
{
    public string boneName = "Pupek";
    [Tooltip("Tuhost pruziny (vyssi = rychlejsi kmitani).")]
    public float stiffness = 160f;
    [Tooltip("Tlumeni (vyssi = rychleji se uklidni).")]
    public float damping = 7f;
    [Tooltip("Jak moc pupek reaguje na zrychleni tela.")]
    public float inertia = 0.3f;
    [Tooltip("Nejvetsi vychyleni (m).")]
    public float maxOffset = 0.035f;

    Transform bone, parent;
    Vector3 restLocal, lastPos, lastVel, offset, velocity;
    bool tracking;

    void Awake()
    {
        bone = FindDeep(transform, boneName);
        if (bone == null || bone.parent == null) { enabled = false; return; }
        parent = bone.parent;
        restLocal = bone.localPosition;
    }

    void OnEnable() => tracking = false;

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        Vector3 pos = parent.position;
        if (!tracking || dt <= 0f || dt > 0.1f || (pos - lastPos).sqrMagnitude > 4f)
        {
            // prvni snimek, pauza nebo teleport: bez skoku
            tracking = dt > 0f;
            lastPos = pos;
            lastVel = Vector3.zero;
            bone.localPosition = restLocal + (parent.lossyScale.x != 0f ? parent.InverseTransformVector(offset) : Vector3.zero);
            return;
        }

        Vector3 vel = (pos - lastPos) / dt;
        Vector3 acc = Vector3.ClampMagnitude((vel - lastVel) / dt, 80f);
        lastPos = pos;
        lastVel = vel;

        // pupek se opozduje za trupem (setrvacnost), pruzina ho tahne zpet
        velocity += (-acc * inertia - offset * stiffness - velocity * damping) * dt;
        offset += velocity * dt;
        if (offset.magnitude > maxOffset)
        {
            offset = offset.normalized * maxOffset;
            velocity *= 0.5f;
        }
        bone.localPosition = restLocal + parent.InverseTransformVector(offset);
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
