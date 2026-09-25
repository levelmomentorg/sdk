// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — links StoreKit.framework into the built Xcode
// project for the storefront plugin (Runtime/Plugins/iOS/LevelMomentStorefront.m).
//
// A plain .m plugin source file does not pull in a non-default framework on
// its own — Unity links only what every player already needs (Foundation,
// UIKit, …). StoreKit is not among those, so the native symbol would fail to
// link without this step.
//
// Editor-only: excluded from every player build by the asmdef's Editor-only
// platform and by living under an "Editor" folder, and excluded from
// tools/unity-compile-check (which compiles Runtime/ only) because there is
// no Unity iOS Xcode-project API to stub outside a real editor install.
// ---------------------------------------------------------------------------

#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace LevelMoment.Editor
{
    internal static class LevelMomentIOSPostProcessBuild
    {
        [PostProcessBuild(1)]
        public static void OnPostProcessBuild(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS)
                return;

            var projectPath = PBXProject.GetPBXProjectPath(buildPath);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            // package.json pins "unity": "2021.3", which always ships the
            // split UnityFramework target — no pre-2019.3 fallback needed.
            var targetGuid = project.GetUnityFrameworkTargetGuid();
            project.AddFrameworkToProject(targetGuid, "StoreKit.framework", false);
            project.WriteToFile(projectPath);
        }
    }
}
#endif
