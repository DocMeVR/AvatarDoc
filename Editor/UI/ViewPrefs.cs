#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using UnityEditor;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Where a view's own remembered preferences (a fold's open state, the tree's saved expansion) are read and
    /// written. A view receives one of these rather than talking to EditorPrefs directly, so a test can hand it a
    /// store that never touches the real one, the same reasoning behind <see cref="ITipStore"/>.
    /// </summary>
    public interface IViewPrefs
    {
        bool GetBool(string key, bool defaultValue);
        void SetBool(string key, bool value);
        string GetString(string key, string defaultValue);
        void SetString(string key, string value);
        void DeleteKey(string key);
    }

    /// <summary>The real store: plain EditorPrefs, shared by every component and every project this Editor opens.</summary>
    public sealed class EditorPrefsViewPrefs : IViewPrefs
    {
        public bool GetBool(string key, bool defaultValue) => EditorPrefs.GetBool(key, defaultValue);

        public void SetBool(string key, bool value) => EditorPrefs.SetBool(key, value);

        public string GetString(string key, string defaultValue) => EditorPrefs.GetString(key, defaultValue);

        public void SetString(string key, string value) => EditorPrefs.SetString(key, value);

        public void DeleteKey(string key) => EditorPrefs.DeleteKey(key);
    }

    /// <summary>An in-memory store for tests and for a detached view: nothing it does ever reaches the real EditorPrefs.</summary>
    public sealed class MemoryViewPrefs : IViewPrefs
    {
        readonly Dictionary<string, bool> bools = new Dictionary<string, bool>();
        readonly Dictionary<string, string> strings = new Dictionary<string, string>();

        public bool GetBool(string key, bool defaultValue) => bools.TryGetValue(key, out var value) ? value : defaultValue;

        public void SetBool(string key, bool value) => bools[key] = value;

        public string GetString(string key, string defaultValue) => strings.TryGetValue(key, out var value) ? value : defaultValue;

        public void SetString(string key, string value) => strings[key] = value;

        public void DeleteKey(string key)
        {
            bools.Remove(key);
            strings.Remove(key);
        }
    }
}
#endif
