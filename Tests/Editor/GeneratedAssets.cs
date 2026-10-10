using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Guards a folder a test writes into. The test notes what the folder holds before it runs and afterwards takes
    /// out only what appeared while it ran, so a bake the creator made by hand keeps everything it had.
    /// </summary>
    public sealed class GeneratedAssetsGuard
    {
        readonly string folder;
        bool noted;
        bool existed;
        HashSet<string> before = new HashSet<string>();

        public GeneratedAssetsGuard(string folder)
        {
            this.folder = folder;
        }

        /// <summary>Reads what the folder holds right now. Runs before the test does anything.</summary>
        public void Note()
        {
            noted = true;
            existed = AssetDatabase.IsValidFolder(folder);
            before = new HashSet<string>(Entries());
        }

        /// <summary>Deletes what appeared since Note, and the folder itself only when Note found none.</summary>
        public void CleanUp()
        {
            // Without a snapshot there is no way to tell the test's own files from the creator's, so nothing goes.
            if (!noted) return;
            if (!AssetDatabase.IsValidFolder(folder)) return;
            if (!existed)
            {
                AssetDatabase.DeleteAsset(folder);
                return;
            }
            foreach (var entry in Entries())
                if (!before.Contains(entry)) AssetDatabase.DeleteAsset(entry);
        }

        /// <summary>The asset paths of the folder's own entries, subfolders included and .meta files left out.</summary>
        IEnumerable<string> Entries()
        {
            var project = Path.GetDirectoryName(Application.dataPath) ?? "";
            var full = Path.Combine(project, folder);
            if (!Directory.Exists(full)) return new List<string>();
            return Directory.GetFileSystemEntries(full)
                .Where(path => !path.EndsWith(".meta"))
                .Select(path => folder + "/" + Path.GetFileName(path))
                .ToList();
        }
    }

    /// <summary>The shared guard for the folder NDMF's Manual Bake keeps a baked avatar's assets in.</summary>
    public static class GeneratedAssets
    {
        public const string Folder = "Assets/ZZZ_GeneratedAssets";

        static readonly GeneratedAssetsGuard guard = new GeneratedAssetsGuard(Folder);

        public static void Note() => guard.Note();

        public static void CleanUp() => guard.CleanUp();

        /// <summary>Makes the folder when it is not there, for a test that writes an asset into it by path.</summary>
        public static void Ensure()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "ZZZ_GeneratedAssets");
        }
    }

    public class GeneratedAssetsTests
    {
        [Test]
        public void GeneratedAssetsCleanupLeavesWhatWasThere()
        {
            // A folder of the test's own directly under Assets: never inside the package, which may ship read only,
            // and named so that no two runs and no real folder can meet it. DeleteAsset takes its .meta with it.
            var name = "AvatarDocGuardTest_" + System.Guid.NewGuid().ToString("N");
            var root = "Assets/" + name;
            var kept = root + "/Kept";
            var fresh = root + "/Fresh";
            try
            {
                AssetDatabase.CreateFolder("Assets", name);
                AssetDatabase.CreateFolder(root, "Kept");
                AssetDatabase.CreateAsset(new AnimationClip { name = "Before" }, kept + "/Before.anim");

                var guard = new GeneratedAssetsGuard(kept);
                guard.Note();
                AssetDatabase.CreateAsset(new AnimationClip { name = "During" }, kept + "/During.anim");
                AssetDatabase.CreateFolder(kept, "Sub");
                guard.CleanUp();

                Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(kept + "/Before.anim"), Is.Not.Null, "what was there before stays");
                Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(kept + "/During.anim"), Is.Null, "what the test made goes");
                Assert.That(AssetDatabase.IsValidFolder(kept + "/Sub"), Is.False, "and so does a folder it made");
                Assert.That(AssetDatabase.IsValidFolder(kept), Is.True, "the folder itself was there before");

                var made = new GeneratedAssetsGuard(fresh);
                made.Note();
                AssetDatabase.CreateFolder(root, "Fresh");
                AssetDatabase.CreateAsset(new AnimationClip { name = "Only" }, fresh + "/Only.anim");
                made.CleanUp();

                Assert.That(AssetDatabase.IsValidFolder(fresh), Is.False, "a folder that was not there goes whole");
            }
            finally
            {
                AssetDatabase.DeleteAsset(root);
            }
        }
    }
}
