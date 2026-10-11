using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Addresses of the online guide, and the button that opens a message's entry in it.</summary>
    public static class DocsLinks
    {
        public const string QuickStartPage = "start-here/first-toggle";
        public const string MessagesPage = "reference/messages";

        /// <summary>Opens an address. Tests replace it so no browser opens.</summary>
        public static Action<string> Open = Application.OpenURL;

        public static string Page(string path) => DocsAddress.Base + path.Trim('/') + "/";

        public static string Anchor(string key) => key.ToLowerInvariant().Replace('.', '-').Replace('_', '-');

        /// <summary>The Messages page has one entry per key; the count-of-one twin shares its base key's entry.</summary>
        public static string Message(string key) => Page(MessagesPage) + "#" + Anchor(BaseKey(key));

        public static bool IsMessageKey(string key)
            => !string.IsNullOrEmpty(key) && (key.StartsWith("ad.error.", StringComparison.Ordinal) || key.StartsWith("ad.warn.", StringComparison.Ordinal) || key.StartsWith("ad.info.", StringComparison.Ordinal));

        static string BaseKey(string key) => key != null && key.EndsWith(".one", StringComparison.Ordinal) ? key.Substring(0, key.Length - 4) : key;

        /// <summary>A link-styled button that opens the key's entry on the Messages page; null when the key is not a message key.</summary>
        public static Button LearnMoreButton(string key)
        {
            if (!IsMessageKey(key)) return null;
            var address = Message(key);
            var button = new Button(() => Open(address)) { text = L.Text("ad.ui.message.learn_more"), name = "ad-learn-more", tooltip = L.Text("ad.ui.message.learn_more.tip") };
            button.AddToClassList("ad-learn-more");
            return button;
        }
    }
}
