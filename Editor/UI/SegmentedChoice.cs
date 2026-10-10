#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>A row of flat buttons standing in for a two or three way dropdown: exactly one item is on, and clicking the chosen one again does nothing.</summary>
    public sealed class SegmentedChoice : VisualElement
    {
        readonly List<Button> items = new List<Button>();

        public int Index { get; private set; }
        public event Action<int> Changed;

        /// <summary>The item buttons in order, for callers that need to name or inspect one directly.</summary>
        public IReadOnlyList<Button> Items => items;

        /// <param name="itemTooltips">One sentence per item, shown on that item's own button instead of only on the whole control. Omitted (the default for every existing caller) leaves each item's tooltip unset, relying on the control's own tooltip.</param>
        public SegmentedChoice(IReadOnlyList<string> labels, int index = 0, IReadOnlyList<string> itemTooltips = null)
        {
            AddToClassList("ad-seg");
            for (int i = 0; i < labels.Count; i++)
            {
                int chosen = i;
                var item = new Button(() => Pick(chosen)) { text = labels[i] };
                if (itemTooltips != null && i < itemTooltips.Count) item.tooltip = itemTooltips[i];
                item.AddToClassList("ad-seg-item");
                items.Add(item);
                Add(item);
            }
            Index = index;
            UpdateSelection();
        }

        /// <summary>Chooses an item exactly as clicking it would, raising Changed unless it is already the chosen one. Public so a click, wired to it above, and a test built with no panel to click on drive the same path.</summary>
        public void Pick(int newIndex)
        {
            if (newIndex == Index) return;
            Index = newIndex;
            UpdateSelection();
            Changed?.Invoke(Index);
        }

        /// <summary>Sets the shown choice without raising Changed, for a control refreshing itself from the model it reads.</summary>
        public void SetIndexWithoutNotify(int newIndex)
        {
            Index = newIndex;
            UpdateSelection();
        }

        void UpdateSelection()
        {
            for (int i = 0; i < items.Count; i++)
                items[i].EnableInClassList("ad-seg-on", i == Index);
        }
    }
}
#endif
