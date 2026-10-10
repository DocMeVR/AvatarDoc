using UnityEditor;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// A folder under Assets that a test may write assets into. The package itself can sit in Packages, where a test
    /// must not leave files, so temporary assets go here and the shared parent is removed again once it is empty.
    /// </summary>
    internal static class TempAssetFolder
    {
        const string Parent = "Assets/TempAssets";

        public static string Create(string name)
        {
            if (!AssetDatabase.IsValidFolder(Parent)) AssetDatabase.CreateFolder("Assets", "TempAssets");
            var folder = Parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Parent, name);
            return folder;
        }

        public static void Delete(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            if (AssetDatabase.IsValidFolder(Parent) && AssetDatabase.FindAssets("", new[] { Parent }).Length == 0) AssetDatabase.DeleteAsset(Parent);
        }
    }
}
