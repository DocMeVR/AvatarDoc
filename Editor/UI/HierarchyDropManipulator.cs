#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Accepts GameObjects dragged from the Hierarchy onto its target element.</summary>
    public sealed class HierarchyDropManipulator : PointerManipulator
    {
        readonly Func<GameObject, bool> accept;
        readonly Action<List<GameObject>> onDrop;
        readonly TrickleDown phase;
        List<GameObject> dragging;

        /// <param name="phase">TrickleDown for a target that holds a control answering drags itself, such as a TreeView: the drag is then looked at before that control sees it, and stopped only when this takes the drop.</param>
        public HierarchyDropManipulator(VisualElement target, Func<GameObject, bool> accept, Action<List<GameObject>> onDrop, TrickleDown phase = TrickleDown.NoTrickleDown)
        {
            this.target = target;
            this.accept = accept;
            this.onDrop = onDrop;
            this.phase = phase;
        }

        bool Early => phase == TrickleDown.TrickleDown;

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<DragEnterEvent>(OnEnter);
            target.RegisterCallback<DragUpdatedEvent>(OnUpdated, phase);
            target.RegisterCallback<DragPerformEvent>(OnPerform, phase);
            target.RegisterCallback<DragLeaveEvent>(OnLeave);
            target.RegisterCallback<DragExitedEvent>(OnLeave, phase);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<DragEnterEvent>(OnEnter);
            target.UnregisterCallback<DragUpdatedEvent>(OnUpdated, phase);
            target.UnregisterCallback<DragPerformEvent>(OnPerform, phase);
            target.UnregisterCallback<DragLeaveEvent>(OnLeave);
            target.UnregisterCallback<DragExitedEvent>(OnLeave, phase);
        }

        void OnEnter(DragEnterEvent e) => Look();

        /// <summary>Reads what is being dragged right now. A control inside the target can swallow the enter and leave events of a drag, so an early target reads again on every update rather than trust what it saw last.</summary>
        void Look()
        {
            dragging = DragAndDrop.objectReferences.OfType<GameObject>().Where(accept).ToList();
            target.EnableInClassList("ad-drop-active", dragging.Count > 0);
        }

        void OnUpdated(DragUpdatedEvent e)
        {
            if (Early || dragging == null) Look();
            bool takes = dragging.Count > 0;
            if (takes) DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
            else if (!Early) DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            if (takes && Early) e.StopPropagation();
        }

        void OnPerform(DragPerformEvent e)
        {
            if (Early) Look();
            var objects = dragging;
            Clear();
            if (objects == null || objects.Count == 0) return;
            DragAndDrop.AcceptDrag();
            onDrop(objects);
            e.StopPropagation();
        }

        void OnLeave(EventBase e) => Clear();

        void Clear()
        {
            dragging = null;
            target.RemoveFromClassList("ad-drop-active");
        }
    }
}
#endif
