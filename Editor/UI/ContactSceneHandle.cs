#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The Scene view handle for the contact trigger selected in one detail panel: a wire sphere at the anchor plus
    /// offset, a position handle and a radius handle. Two open views can show the same trigger (the inspector and
    /// the AvatarDoctor window, or two inspectors), so drawing is arbitrated by a static registry keyed by the
    /// owning component's instance id plus the entry id: only the first attached, matching handle in the list
    /// actually subscribes to SceneView.duringSceneGui, so only one sphere is ever drawn for one trigger. When that
    /// handle goes away, the next one registered for the same key takes over.
    /// </summary>
    public sealed class ContactSceneHandle
    {
        static readonly Dictionary<string, List<ContactSceneHandle>> claimants = new Dictionary<string, List<ContactSceneHandle>>();

        readonly EntryDetailView view;
        bool attached;
        string claimedKey;
        bool subscribed;

        public ContactSceneHandle(EntryDetailView view)
        {
            this.view = view;
        }

        /// <summary>Whether this handle currently owns the Scene view subscription (is actually drawing), as opposed to merely waiting behind another view showing the same trigger. Public for tests, which drive the registry without a real Scene view.</summary>
        public bool IsDrawing => subscribed;

        /// <summary>Marks the view's panel as ready and syncs against row at once, so a freshly opened view whose restored selection is already a contact trigger claims it immediately rather than waiting for the next refresh.</summary>
        public void Attach(TreeRow row)
        {
            attached = true;
            Sync(row);
        }

        /// <summary>Withdraws from the registry unconditionally. Called when the view leaves its panel, so a closed inspector or window never keeps a claim (or a subscription) alive.</summary>
        public void Detach()
        {
            attached = false;
            Claim(null);
        }

        /// <summary>Re-evaluates the claim against row: whether the condition it has selected is a contact of an enabled, own component while this handle is attached. Called on every Show and Refresh, so a selection change or the row losing its contact source lets go of the claim.</summary>
        public void Sync(TreeRow row)
        {
            Claim(attached && Shows(row, view.TouchConditionOf(row)) ? KeyFor(row) : null);
        }

        void Claim(string key)
        {
            if (key == claimedKey) { UpdateSubscription(); return; }
            Withdraw();
            claimedKey = key;
            if (key != null)
            {
                if (!claimants.TryGetValue(key, out var list)) claimants[key] = list = new List<ContactSceneHandle>();
                list.Add(this);
            }
            UpdateSubscription();
        }

        void Withdraw()
        {
            if (claimedKey == null) return;
            if (claimants.TryGetValue(claimedKey, out var list))
            {
                bool wasDrawer = list.Count > 0 && list[0] == this;
                list.Remove(this);
                if (list.Count == 0) claimants.Remove(claimedKey);
                else if (wasDrawer) list[0].UpdateSubscription();
            }
            claimedKey = null;
            if (subscribed) { SceneView.duringSceneGui -= OnSceneGui; subscribed = false; }
        }

        void UpdateSubscription()
        {
            bool shouldDraw = claimedKey != null && claimants.TryGetValue(claimedKey, out var list) && list.Count > 0 && list[0] == this;
            if (shouldDraw == subscribed) return;
            if (shouldDraw) { SceneView.duringSceneGui += OnSceneGui; subscribed = true; }
            else { SceneView.duringSceneGui -= OnSceneGui; subscribed = false; }
        }

        static bool Shows(TreeRow row, WhenCondition condition)
        {
            return row != null && row.Mine && row.Entry != null && row.Entry.kind == EntryKind.Trigger
                && condition != null && condition.kind == WhenConditionKind.Contact && condition.contactAnchor != null
                && row.Owner != null && row.Owner.enabled && !row.Owner.locked;
        }

        string KeyFor(TreeRow row) => row.Owner.GetInstanceID() + ":" + row.Entry.id + ":" + view.SelectedConditionKey;

        void OnSceneGui(SceneView sceneView)
        {
            var row = view.Row;
            var condition = view.TouchConditionOf(row);
            if (!Shows(row, condition)) { Withdraw(); return; }
            var anchor = condition.contactAnchor;
            float scale = Mathf.Max(0.0001f, anchor.lossyScale.x);
            Vector3 centre = anchor.TransformPoint(condition.contactOffset);
            float worldRadius = condition.contactRadius * scale;

            Handles.color = AccentColor();
            Handles.DrawWireDisc(centre, Vector3.up, worldRadius);
            Handles.DrawWireDisc(centre, Vector3.right, worldRadius);
            Handles.DrawWireDisc(centre, Vector3.forward, worldRadius);

            EditorGUI.BeginChangeCheck();
            var newCentre = Handles.PositionHandle(centre, anchor.rotation);
            var newWorldRadius = Handles.RadiusHandle(anchor.rotation, centre, worldRadius);
            if (EditorGUI.EndChangeCheck())
            {
                var newOffset = anchor.InverseTransformPoint(newCentre);
                var newRadius = newWorldRadius / scale;
                var stored = view.StoredTouchCondition;
                if (stored != null) Apply(stored, row.Owner, newOffset, newRadius);
                else Apply(row.Entry, row.Owner, newOffset, newRadius);
                view.RefreshTouchFields();
            }
        }

        /// <summary>Writes the offset and radius the handles report onto one condition of a trigger's rule, with Undo. The overload below does the same on a trigger that still works from its flat source fields.</summary>
        public static void Apply(WhenCondition condition, AvatarDoc component, Vector3 newOffsetLocal, float newRadius)
        {
            if (condition == null || EntryEditing.Refuses(component)) return;
            Undo.RecordObject(component, "Move contact");
            condition.contactOffset = newOffsetLocal;
            condition.contactRadius = Mathf.Clamp(newRadius, 0.005f, 1f);
            EntryEditing.MarkChanged(component);
            NoteEdit(component);
        }

        /// <summary>Writes the offset and radius the handles report, with Undo, clamps the radius and marks the component changed. The testable core: no SceneView needed to call it.</summary>
        public static void Apply(MenuEntry entry, AvatarDoc component, Vector3 newOffsetLocal, float newRadius)
        {
            if (entry == null || EntryEditing.Refuses(component)) return;
            Undo.RecordObject(component, "Move contact");
            entry.contactOffset = newOffsetLocal;
            entry.contactRadius = Mathf.Clamp(newRadius, 0.005f, 1f);
            EntryEditing.MarkChanged(component);
            NoteEdit(component);
        }

        static void NoteEdit(AvatarDoc component)
        {
            // Neither field reaches the plan, and the handle redraws the panel's own fields itself, so the change
            // event this write publishes at the end of the frame has nothing left to do: a drag would otherwise
            // rebuild the whole model once per pointer move. The cosmetic note takes that event's place, so a
            // second view of the same trigger still follows the drag and a real change coalesced into the same
            // batch is not swallowed with it.
            AvatarPlanCache.NoteSelfEdit(component);
            AvatarPlanCache.NoteCosmetic(AvatarPlanCache.RootOf(component));
        }

        static Color AccentColor()
        {
            return EditorGUIUtility.isProSkin ? new Color(0.247f, 0.788f, 0.659f) : new Color(0.071f, 0.541f, 0.431f);
        }
    }
}
#endif
