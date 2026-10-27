// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — links StoreKit.framework into the built Xcode
// project for the storefront plugin (Runtime/Plugins/iOS/LevelMomentStorefront.m).
//
// A plain .m plugin source file does not pull in a non-default framework on
// its own — Unity links only what every player already needs (Foundation,
// UIKit, …). StoreKit is not among those, so the native symbol would fail to
// link without this step.
//
// The step checks the build target at run time instead of compiling behind
// #if UNITY_IOS. Editor scripts compile for the editor's active target, and a
// batch build that names iOS only in BuildPlayerOptions (no -buildTarget iOS)
// keeps the scripts compiled for the previous target, so an #if UNITY_IOS
// callback never ran and the Xcode link failed on SKPaymentQueue. The Xcode
// API (UnityEditor.iOS.Extensions.Xcode) ships with iOS Build Support and is
// not referenceable without it, so it is reached by reflection.
//
// Editor-only: excluded from every player build by the asmdef's Editor-only
// platform and by living under an "Editor" folder. tools/unity-compile-check
// compiles Runtime/ only, so this file needs a real editor to compile.
// ---------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;

namespace LevelMoment.Editor
{
    internal static class LevelMomentIOSPostProcessBuild
    {
        const string XcodeAssembly = "UnityEditor.iOS.Extensions.Xcode";
        const string PbxProjectTypeName = "UnityEditor.iOS.Xcode.PBXProject";

        [PostProcessBuild(1)]
        public static void OnPostProcessBuild(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS)
                return;

            var type = FindPbxProjectType();
            if (type == null)
                throw new BuildFailedException(
                    "[LevelMoment] Couldn't load Unity's Xcode project API (" + XcodeAssembly + "), so " +
                    "StoreKit.framework wasn't linked and the Xcode build will fail on SKPaymentQueue. " +
                    "Install iOS Build Support for this editor, then build again.");

            try
            {
                var projectPath = (string)Method(type, "GetPBXProjectPath", typeof(string)).Invoke(null, new object[] { buildPath });
                var project = Activator.CreateInstance(type);
                Method(type, "ReadFromFile", typeof(string)).Invoke(project, new object[] { projectPath });

                // package.json pins "unity": "2021.3", which always ships the
                // split UnityFramework target — no pre-2019.3 fallback needed.
                var targetGuid = (string)Method(type, "GetUnityFrameworkTargetGuid").Invoke(project, null);
                Method(type, "AddFrameworkToProject", typeof(string), typeof(string), typeof(bool))
                    .Invoke(project, new object[] { targetGuid, "StoreKit.framework", false });
                Method(type, "WriteToFile", typeof(string)).Invoke(project, new object[] { projectPath });
            }
            catch (TargetInvocationException e)
            {
                throw new BuildFailedException(
                    "[LevelMoment] Couldn't link StoreKit.framework: " + (e.InnerException ?? e).Message);
            }
        }

        static MethodInfo Method(Type type, string name, params Type[] parameters)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static, null, parameters, null);
            if (method == null)
                throw new BuildFailedException(
                    "[LevelMoment] Unity's Xcode project API has no " + name + ", so StoreKit.framework wasn't linked.");
            return method;
        }

        // Loaded already in most editors with iOS Build Support; otherwise read
        // from the iOS playback engine folder.
        static Type FindPbxProjectType()
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == XcodeAssembly);
            if (loaded != null)
                return loaded.GetType(PbxProjectTypeName);

            try
            {
                var engine = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.iOS, BuildOptions.None);
                var dll = Path.Combine(engine, XcodeAssembly + ".dll");
                return File.Exists(dll) ? Assembly.LoadFrom(dll).GetType(PbxProjectTypeName) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
