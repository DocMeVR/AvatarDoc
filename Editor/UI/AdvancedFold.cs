#if DOCME_NDMF && DOCME_VRCSDK3A
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The one Advanced fold every entry panel shares, at its bottom: closed by default, remembered per entry the
    /// same way the header's Component settings fold remembers itself per component, with the same non default
    /// dot. Built once per panel rebuild; a panel puts whatever of Platform, Parameter, Saved and Synced, Blend
    /// time and (for a trigger, while unused) the gate into <see cref="Content"/>, then adds this at the bottom.
    /// </summary>
    public sealed class AdvancedFold : VisualElement
    {
        readonly Foldout foldout;
        readonly VisualElement dot = new VisualElement { name = "ad-advanced-fold-dot" };
        readonly VisualElement content = new VisualElement { name = "ad-advanced-fold-content" };
        readonly IViewPrefs prefs;
        readonly string key;
        bool open;
        bool nonDefault;
        bool hiddenWhileDefault;
        bool shownSinceBuilt;

        public AdvancedFold(string foldKey, IViewPrefs viewPrefs)
        {
            key = foldKey;
            prefs = viewPrefs;
            name = "ad-advanced-fold";
            foldout = new Foldout { name = "ad-advanced-fold-toggle", text = L.Text("ad.ui.advanced_fold"), tooltip = L.Text("ad.ui.advanced_fold.tip") };
            foldout.AddToClassList("ad-fold");
            var toggle = foldout.Q<Toggle>();
            if (toggle != null) toggle.tooltip = L.Text("ad.ui.advanced_fold.tip");
            dot.AddToClassList("ad-dot");
            dot.tooltip = L.Text("ad.ui.advanced_fold.dot.tip");
            var inputRow = toggle?.Q(className: "unity-foldout__input");
            (inputRow ?? (VisualElement)toggle ?? foldout).Add(dot);
            foldout.RegisterValueChangedCallback(e =>
            {
                open = e.newValue;
                ComponentFold.Save(prefs, key, open);
                Apply();
            });
            Add(foldout);
            content.name = "ad-advanced-fold-content";
            Add(content);
            open = ComponentFold.Load(prefs, key);
            Apply();
        }

        /// <summary>Where a panel adds the fields this fold hides while closed.</summary>
        public VisualElement Content => content;

        /// <summary>Whether the fold is open right now. Read by tests.</summary>
        public bool IsOpen => open;

        /// <summary>Whether the fold's own dot is showing. Read by tests.</summary>
        public bool DotVisible => !dot.ClassListContains("ad-hidden");

        /// <summary>
        /// Leaves the whole fold out while everything in it is at its default, which is what Simple does; it shows,
        /// closed and with its dot, the moment something in it is not, so a value the creator set is never hidden.
        /// Once shown it stays, even when its last changed value is put back, so a fold the creator has open does not
        /// vanish under the pointer; the panel built again for another entry decides afresh.
        /// </summary>
        public bool HiddenWhileDefault
        {
            get => hiddenWhileDefault;
            set
            {
                hiddenWhileDefault = value;
                ApplyVisibility();
            }
        }

        /// <summary>Whether the fold has been shown since it was built. The panel hands this on to the fold it builds again for the same entry.</summary>
        public bool ShownSinceBuilt => shownSinceBuilt;

        /// <summary>Shows the fold from the start, for an entry whose fold was already shown before its panel was built again.</summary>
        public void KeepShown()
        {
            shownSinceBuilt = true;
            ApplyVisibility();
        }

        /// <summary>Opens or closes the fold and remembers the choice for this entry. Public so a test can drive it the same way clicking the fold's own header does.</summary>
        public void ToggleOpen()
        {
            open = !open;
            ComponentFold.Save(prefs, key, open);
            Apply();
        }

        void Apply()
        {
            foldout.SetValueWithoutNotify(open);
            content.EnableInClassList("ad-hidden", !open);
            // Re-derives from the last known non default flag, since opening or closing the fold itself (with no
            // panel refresh in between) has to update the dot on its own: this component has no other way to
            // learn whether its own content still holds a non default value.
            dot.EnableInClassList("ad-hidden", open || !nonDefault);
        }

        /// <summary>The dot shows only while the fold is closed and something inside it is not at its default.</summary>
        public void SetNonDefault(bool value)
        {
            nonDefault = value;
            dot.EnableInClassList("ad-hidden", open || !nonDefault);
            ApplyVisibility();
        }

        void ApplyVisibility()
        {
            if (!hiddenWhileDefault || nonDefault) shownSinceBuilt = true;
            EnableInClassList("ad-hidden", !shownSinceBuilt);
        }
    }
}
#endif
