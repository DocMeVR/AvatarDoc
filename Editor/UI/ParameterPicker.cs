#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Every parameter name a trigger can watch, in an AdMenu with a search field.</summary>
    public static class ParameterPicker
    {
        /// <summary>For callers outside AvatarDoc, which hand over only the button's world rectangle: the button is looked for with AdMenu.FindTarget.</summary>
        public static void Show(Rect anchor, AvatarPlan plan, Action<string> onPick) => Open(anchor, AdMenu.FindTarget(anchor), plan, onPick);

        /// <summary>Opens the picker on the control that asked for it.</summary>
        internal static void Show(VisualElement anchor, AvatarPlan plan, Action<string> onPick) => Open(anchor.worldBound, anchor, plan, onPick);

        /// <summary>Opens the picker under anchor in the window that hosts target. Null when target is not on a panel.</summary>
        public static AdMenu Open(Rect anchor, VisualElement target, AvatarPlan plan, Action<string> onPick) =>
            AdMenu.Open(anchor, target, Items(plan, onPick), search: true);

        /// <summary>
        /// The avatar's own parameters, AvatarDoc's, VRChat's built in ones and VRCFury's globals, a group each, with
        /// the type as the dim text beside each name. A name that appears in more than one group (an AvatarDoc toggle
        /// already visible as an avatar parameter) is only ever shown once, in the first group it turns up in; a
        /// group with nothing left in it is not shown.
        /// </summary>
        public static List<AdMenu.Item> Items(AvatarPlan plan, Action<string> onPick)
        {
            var groups = new List<AdMenu.Item>();
            var seen = new HashSet<string>();
            void AddGroup(string title, IEnumerable<(string Name, string Type)> names)
            {
                var children = new List<AdMenu.Item>();
                foreach (var (name, type) in names)
                {
                    if (!seen.Add(name)) continue;
                    children.Add(new AdMenu.Item { Text = name, Note = type, Action = () => onPick(name) });
                }
                if (children.Count > 0) groups.Add(new AdMenu.Item { Text = title, Children = children });
            }

            var avatarRoot = plan?.AvatarRoot;
            if (avatarRoot != null)
            {
                AddGroup(L.Text("ad.ui.pick.parameter.avatar"), AvatarPlanCache.ProvidedFor(avatarRoot)
                    .Where(p => !(p.Source is AvatarDoc)).OrderBy(p => p.EffectiveName)
                    .Select(p => (p.EffectiveName, p.ParameterType.HasValue ? p.ParameterType.Value.ToString() : null)));
            }
            if (plan?.Plan != null)
                AddGroup(L.Text("ad.ui.pick.parameter.doctor"), plan.Plan.Parameters.OrderBy(p => p.Name).Select(p => (p.Name, p.Type.ToString())));
            AddGroup(L.Text("ad.ui.pick.parameter.vrchat"), TriggerPlanner.BuiltInNames.OrderBy(n => n).Select(n => (n, TriggerPlanner.BuiltInType(n).ToString())));
            AddGroup(L.Text("ad.ui.chip.vrcfury"), VrcFuryChoices(avatarRoot).Select(n => (n, (string)null)));
            return groups;
        }

        /// <summary>The distinct VRCFury global parameter names to offer, in order.</summary>
        public static List<string> VrcFuryChoices(Transform avatarRoot)
        {
            var result = new List<string>();
            if (avatarRoot == null) return result;
            var seen = new HashSet<string>();
            foreach (var name in VrcFuryReader.GlobalParameters(avatarRoot).OrderBy(n => n, StringComparer.Ordinal))
            {
                if (seen.Add(name)) result.Add(name);
            }
            return result;
        }
    }
}
#endif
