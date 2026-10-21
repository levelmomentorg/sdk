// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — keeps legacy input working beside the Input System.
//
// The gree WebView needs the Input System package, which this package depends
// on. A project on Active Input Handling "Input Manager (Old)" is then offered a
// switch to the new backends only, which breaks every legacy Input call. This
// sets "Both" instead, once, and only from "Old": a project already on "New" or
// "Both" is left alone. The change takes effect when the editor next starts.
// ---------------------------------------------------------------------------

using UnityEditor;
using UnityEngine;

namespace LevelMoment.Editor
{
    [InitializeOnLoad]
    internal static class LevelMomentInputHandling
    {
        const int Old = 0;
        const int Both = 2;

        static LevelMomentInputHandling()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var settings = new SerializedObject(assets[0]);
            var handler = settings.FindProperty("activeInputHandler");
            if (handler == null || handler.intValue != Old) return;
            handler.intValue = Both;
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(assets[0]);
            Debug.Log("[LevelMoment] Set Active Input Handling to Both, so legacy input keeps working beside the " +
                "Input System package the WebView needs. Restart the editor for it to take effect.");
        }
    }
}
