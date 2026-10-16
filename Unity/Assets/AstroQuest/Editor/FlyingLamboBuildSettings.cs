using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public sealed class FlyingLamboBuildSettings : IPreprocessBuildWithReport
{
    // Meta's order-1 processor injects the local editor address and access token.
    // Clear them afterwards so regular APKs can be shared independently.
    public int callbackOrder => int.MaxValue;

    public void OnPreprocessBuild(BuildReport report)
    {
        if ((report.summary.options & BuildOptions.Development) != 0) return;
        var asset = AssetDatabase.LoadMainAssetAtPath("Assets/Resources/DevAgentSettings.asset");
        if (asset == null) return;
        var settings = new SerializedObject(asset);
        settings.FindProperty("enabled").boolValue = false;
        settings.FindProperty("serverAddress").stringValue = "";
        settings.FindProperty("accessToken").stringValue = "";
        settings.FindProperty("witClientAccessToken").stringValue = "";
        settings.FindProperty("witConfiguration").objectReferenceValue = null;
        settings.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(asset);
    }
}
