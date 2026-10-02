using System.Runtime.InteropServices;
using UnityEngine;

// Windows funkce "Jednim prstem" (Sticky Keys) prida Shiftu pamet: prvni tuknuti ho prichyti, druhe pusti,
// takze hra vidi jen kazdy druhy stisk. Zapne se sama, kdyz hrac 5x rychle zmackne Shift.
// Stejne jako velke hry ji proto po dobu, kdy ma hra fokus, vypneme a pri odchodu vratime puvodni stav.
// Meni se jen stav pro bezici prihlaseni (nic se neuklada do nastaveni Windows).
public class StickyKeysGuard : MonoBehaviour
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [StructLayout(LayoutKind.Sequential)]
    struct StickyKeys
    {
        public uint cbSize;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    static extern bool SystemParametersInfo(uint action, uint param, ref StickyKeys data, uint winIni);

    const uint GetStickyKeys = 0x003A;
    const uint SetStickyKeys = 0x003B;
    const uint On = 0x1;
    const uint HotkeyActive = 0x4;
    const uint ConfirmHotkey = 0x8;

    uint original;
    bool haveOriginal;
    bool suppressed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("StickyKeysGuard");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        go.AddComponent<StickyKeysGuard>();
    }

    void Start()
    {
        if (Application.isFocused)
            Suppress();
    }

    void OnApplicationFocus(bool focused)
    {
        if (focused) Suppress();
        else Restore();
    }

    void OnApplicationQuit()
    {
        Restore();
    }

    void OnDestroy()
    {
        Restore();
    }

    void Suppress()
    {
        if (suppressed) return;

        var state = new StickyKeys { cbSize = (uint)Marshal.SizeOf(typeof(StickyKeys)) };
        if (!SystemParametersInfo(GetStickyKeys, state.cbSize, ref state, 0)) return;

        original = state.dwFlags;
        haveOriginal = true;

        state.dwFlags &= ~(On | HotkeyActive | ConfirmHotkey);
        if (state.dwFlags != original && SystemParametersInfo(SetStickyKeys, state.cbSize, ref state, 0))
            suppressed = true;
    }

    void Restore()
    {
        if (!suppressed || !haveOriginal) return;

        var state = new StickyKeys { cbSize = (uint)Marshal.SizeOf(typeof(StickyKeys)), dwFlags = original };
        SystemParametersInfo(SetStickyKeys, state.cbSize, ref state, 0);
        suppressed = false;
    }
#endif
}
