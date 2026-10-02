using UnityEditor;

// Kdyz do projektu pribude (nebo se zmeni) avatar hrdiny, pusti nastaveni znovu.
// Bez toho by se novy model priradil hrdinovi az po dalsim prekompilovani skriptu.
public class AvatarImportWatcher : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var path in imported)
        {
            if (path.StartsWith("Assets/Models/") && path.EndsWith("Avatar.fbx"))
            {
                EditorApplication.delayCall += M7Setup.RunIfNeeded;
                return;
            }
        }
    }
}
