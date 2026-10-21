// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — carries the project's sandbox token into development
// builds, and keeps it out of release builds.
//
// The token lives in UserSettings/LevelMomentSandboxToken.txt, outside Assets,
// so no build packs it by accident. For a development build this copies it into
// a generated Resources folder for the length of the build and deletes it after;
// a release build gets nothing. A token file still under Assets/**/Resources
// (the 0.3.1 layout) fails a release build, because Unity packs every Resources
// file into every build.
//
// The generated folder carries its own .gitignore. It is deleted after the build
// whether the build succeeded or not (a failed build never reaches the
// postprocess step, so a delayCall does it), and again when the editor loads.
// ---------------------------------------------------------------------------

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace LevelMoment.Editor
{
    [InitializeOnLoad]
    internal sealed class LevelMomentSandboxBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        const string TokenFile = "UserSettings/LevelMomentSandboxToken.txt";
        const string GeneratedFolder = "Assets/LevelMomentSDK.Generated";
        const string ResourceFile = GeneratedFolder + "/Resources/LevelMomentSandboxToken.txt";

        static LevelMomentSandboxBuild()
        {
            EditorApplication.delayCall += RemoveGenerated;
        }

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var development = (report.summary.options & BuildOptions.Development) != 0;
            if (development)
            {
                RemoveGenerated();
                CopyTokenIn();
                return;
            }

            // A release build fails closed: Unity logs and continues past any
            // exception that isn't a BuildFailedException.
            string[] legacy;
            try
            {
                RemoveGenerated();
                legacy = LegacyTokenFiles();
            }
            catch (System.Exception e)
            {
                throw new BuildFailedException(
                    "[LevelMoment] Couldn't confirm this release build carries no sandbox token: " + e.Message);
            }
            if (Directory.Exists(GeneratedFolder) || AssetDatabase.IsValidFolder(GeneratedFolder))
                throw new BuildFailedException(
                    "[LevelMoment] " + GeneratedFolder + " still holds a development build's sandbox token. " +
                    "Delete the folder, then build again.");
            if (legacy.Length > 0)
                throw new BuildFailedException(
                    "[LevelMoment] " + legacy[0] + " would ship the sandbox token in this release build. " +
                    "Move it to " + TokenFile + ", which development builds read and release builds never include.");
        }

        static void CopyTokenIn()
        {
            if (!File.Exists(TokenFile)) return;
            // Runs once the synchronous build returns, success or failure.
            EditorApplication.delayCall += RemoveGenerated;
            Directory.CreateDirectory(Path.GetDirectoryName(ResourceFile));
            File.WriteAllText(Path.Combine(GeneratedFolder, ".gitignore"), "*\n");
            File.Copy(TokenFile, ResourceFile, true);
            AssetDatabase.ImportAsset(ResourceFile, ImportAssetOptions.ForceSynchronousImport);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            RemoveGenerated();
        }

        static void RemoveGenerated()
        {
            if (!Directory.Exists(GeneratedFolder)) return;
            if (AssetDatabase.DeleteAsset(GeneratedFolder)) return;
            Directory.Delete(GeneratedFolder, true);
            if (File.Exists(GeneratedFolder + ".meta")) File.Delete(GeneratedFolder + ".meta");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        // Any token file under a Resources folder, at any depth, in Assets or an
        // embedded package: Unity packs all of them into every build.
        static string[] LegacyTokenFiles()
        {
            return new[] { "Assets", "Packages" }
                .Where(Directory.Exists)
                .SelectMany(root => Directory.GetFiles(root, "LevelMomentSandboxToken.*", SearchOption.AllDirectories))
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !p.EndsWith(".meta") && p.Split('/').Contains("Resources"))
                .ToArray();
        }
    }
}
