using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

// PerfProbe ve vyvojovem buildu: casy procesoru a grafiky (FrameTimingManager) potrebuji Frame Timing Stats.
// V beznem buildu se vypnou - nepouzivaji se a neco stoji.
public class PerfProbeBuildSetup : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        PlayerSettings.enableFrameTimingStats = (report.summary.options & BuildOptions.Development) != 0;
    }
}
