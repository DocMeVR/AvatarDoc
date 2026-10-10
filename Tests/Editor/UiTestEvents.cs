#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Linq;
using System.Reflection;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Drives a real control the way a click does, for a test that wants to prove the actual rendered element
    /// works rather than calling the method behind it directly. UI Toolkit's own Button exposes no public way to
    /// fire a click without a real pointer down and up pair through a panel, so this reaches the private delegate
    /// its Clickable manipulator already keeps (the field literally named "clicked") through reflection instead.
    /// </summary>
    public static class UiTestEvents
    {
        public static void SimulateClick(Button button) => SimulateClick(button.clickable);

        /// <summary>
        /// The same reflection reach-through as the Button overload, for a plain element that carries its own
        /// Clickable manipulator instead of being a Button (the header's status text, clickable to match its own
        /// disclosure arrow).
        /// </summary>
        public static void SimulateClick(Clickable clickable)
        {
            var field = clickable.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f => f.Name == "clicked");
            if (field == null)
                throw new System.InvalidOperationException("Clickable has no private field named \"clicked\" in this Unity version, so a click cannot be simulated; update UiTestEvents.SimulateClick");
            if (!(field.GetValue(clickable) is System.Action clicked))
                throw new System.InvalidOperationException("this Clickable has no click handler to simulate");
            clicked.Invoke();
        }
    }
}
#endif
