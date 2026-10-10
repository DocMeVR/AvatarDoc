#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Searchable dropdown of every parameter name a trigger can watch.</summary>
    public sealed class ParameterPicker : AdvancedDropdown
    {
        sealed class NameItem : AdvancedDropdownItem
        {
            public readonly string Value;
            public NameItem(string label, string value) : base(label) { Value = value; }
        }

        readonly AvatarPlan plan;
        readonly Action<string> onPick;

        ParameterPicker(AvatarPlan plan, Action<string> onPick) : base(new AdvancedDropdownState())
        {
            this.plan = plan;
            this.onPick = onPick;
            minimumSize = new Vector2(280f, 340f);
        }

        public static void Show(Rect anchor, AvatarPlan plan, Action<string> onPick)
        {
            new ParameterPicker(plan, onPick).Show(anchor);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(L.Text("ad.ui.pick.parameter"));
            // A name that appears in more than one group (e.g. an AvatarDoctor toggle already visible as an
            // avatar parameter) is only ever shown once, in the first group it turns up in.
            var seen = new HashSet<string>();
            var avatarGroup = new AdvancedDropdownItem(L.Text("ad.ui.pick.parameter.avatar"));
            if (plan.AvatarRoot != null)
            {
                foreach (var p in AvatarPlanCache.ProvidedFor(plan.AvatarRoot).Where(p => !(p.Source is AvatarDoc)).OrderBy(p => p.EffectiveName))
                {
                    if (!seen.Add(p.EffectiveName)) continue;
                    avatarGroup.AddChild(new NameItem(p.ParameterType.HasValue ? L.Aside(p.EffectiveName, p.ParameterType.Value.ToString()) : p.EffectiveName, p.EffectiveName));
                }
            }
            root.AddChild(avatarGroup);
            var doctorGroup = new AdvancedDropdownItem(L.Text("ad.ui.pick.parameter.doctor"));
            foreach (var p in plan.Plan.Parameters.OrderBy(p => p.Name))
            {
                if (!seen.Add(p.Name)) continue;
                doctorGroup.AddChild(new NameItem(L.Aside(p.Name, p.Type.ToString()), p.Name));
            }
            root.AddChild(doctorGroup);
            var vrchatGroup = new AdvancedDropdownItem(L.Text("ad.ui.pick.parameter.vrchat"));
            foreach (var name in TriggerPlanner.BuiltInNames.OrderBy(n => n))
            {
                if (!seen.Add(name)) continue;
                vrchatGroup.AddChild(new NameItem(L.Aside(name, TriggerPlanner.BuiltInType(name).ToString()), name));
            }
            root.AddChild(vrchatGroup);
            var vrcFuryGroup = new AdvancedDropdownItem(L.Text("ad.ui.chip.vrcfury"));
            foreach (var name in VrcFuryChoices(plan.AvatarRoot))
            {
                if (!seen.Add(name)) continue;
                vrcFuryGroup.AddChild(new NameItem(name, name));
            }
            root.AddChild(vrcFuryGroup);
            return root;
        }

        /// <summary>The distinct VRCFury global parameter names to offer, in order. A separate, testable method
        /// because AdvancedDropdownItem's children are not readable from outside UnityEditor, so BuildRoot's tree
        /// cannot be inspected directly by a test.</summary>
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

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is NameItem name) onPick(name.Value);
        }
    }
}
#endif
