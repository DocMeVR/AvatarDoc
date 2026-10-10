#if DOCME_NDMF && DOCME_VRCSDK3A
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>One dismissible first-use line at the top of a panel kind: what it is for and the first thing to do, gone for good once its close cross is clicked.</summary>
    public static class TipBanner
    {
        /// <summary>Builds the tip for tipKind, or null when it was already dismissed: nothing at all is added to the panel in that case, rather than an empty row.</summary>
        public static VisualElement Build(AvatarDoctorView owner, string tipKind, string textKey)
        {
            if (owner.TipStore.IsDismissed(tipKind)) return null;
            var box = new VisualElement { name = "ad-tip-" + tipKind };
            box.AddToClassList("ad-tip");
            var label = new Label(L.Text(textKey)) { name = "ad-tip-text" };
            label.AddToClassList("ad-tip-text");
            box.Add(label);
            var close = new Button(() =>
            {
                owner.TipStore.Dismiss(tipKind);
                box.RemoveFromHierarchy();
            })
            { text = "×", name = "ad-tip-close", tooltip = L.Text("ad.ui.tip.dismiss.tip") };
            close.AddToClassList("ad-tip-close");
            box.Add(close);
            return box;
        }
    }
}
#endif
