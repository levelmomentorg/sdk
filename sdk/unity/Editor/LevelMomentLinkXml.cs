// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — keeps the WebView provider out of UnityLinker's reach.
//
// The gree adapter registers itself through [RuntimeInitializeOnLoadMethod] and
// nothing references it statically, so il2cpp builds strip it and the player
// reports no WebView provider. Unity ignores a link.xml inside a git- or
// file-referenced package, so this hands the linker one for every build and the
// game needs no Assets/link.xml. It preserves only the assemblies the project
// compiles for the player; naming a missing one fails the link.
// ---------------------------------------------------------------------------

using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace LevelMoment.Editor
{
    internal sealed class LevelMomentLinkXml : IUnityLinkerProcessor
    {
        static readonly string[] Preserved = { "LevelMomentSDK.Runtime", "LevelMomentSDK.Gree", "unity-webview" };

        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var xml = new StringBuilder("<linker>\n");
            var player = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                .Select(a => a.name).ToArray();
            foreach (var name in Preserved.Where(n => player.Contains(n)))
                xml.Append("  <assembly fullname=\"").Append(name).Append("\" preserve=\"all\" />\n");
            xml.Append("</linker>\n");

            var path = Path.GetFullPath(Path.Combine("Temp", "LevelMomentSDK", "link.xml"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, xml.ToString());
            return path;
        }
    }
}
