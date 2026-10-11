#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The exact, already-deduped names of every pin layer AvatarDoctorBuilder.Run creates, filled by
    /// AnimatorBuilder.BuildPinLayer as it goes. A layer's display name can be spoofed, by an entry named to look
    /// like one or by a merged controller shipping a layer of that exact name; this set is what the build itself
    /// made, so the final pass can move exactly those layers, and only those, without opening the animator
    /// services again to find out whether there is anything to move.
    /// </summary>
    public sealed class PinLayerState
    {
        public bool Ready;
        public readonly HashSet<string> Names = new HashSet<string>();
        /// <summary>True when there is a named pin layer to move, a binding to release, a shape to copy or a rule curve to check. Follow distance's own layer, its shared tracking pause layers and every blend shape trigger's layer are named in here too, since they are all built as pin layers.</summary>
        public bool HasAny => Names.Count > 0 || Bindings.Count > 0 || PropertyRests.Count > 0 || ShapeMirrors.Count > 0 || HeldCurves.Count > 0;

        /// <summary>
        /// One blend shape trigger the final pass still has to connect to its shape: every FX clip that moves the
        /// shape by then gets a curve on Parameter that copies the shape's own curve, and with Replace the shape's
        /// curve itself goes. Done in the final pass so clips Modular Avatar and VRCFury merge in are reached too.
        /// A null Parameter is a Replace trigger that built no layer: its shape's curves stay, and the pass says so.
        /// </summary>
        public sealed class ShapeMirror
        {
            public MenuEntry Entry;
            public SkinnedMeshRenderer Renderer;
            public string Shape;
            public string Parameter;
            public bool Replace;
        }

        public readonly List<ShapeMirror> ShapeMirrors = new List<ShapeMirror>();

        /// <summary>The pause flags of the driven layers actually built, for BuildTrackingPauseLayers. A layer that was skipped wrote no flag and is not here.</summary>
        public readonly List<TouchBuilder.PauseFlag> PauseFlags = new List<TouchBuilder.PauseFlag>();

        /// <summary>The name Follow distance's shared blink pause layer was built with, if any trigger needed it, so the final pass can drop it when another system already owns Eyes tracking.</summary>
        public string BlinkPauseLayerName;
        /// <summary>The same for the shared lip sync pause layer, for Mouth tracking.</summary>
        public string LipSyncPauseLayerName;

        /// <summary>One blend shape binding a pin layer writes in its on state or member states, and the scene
        /// weight it held before any AvatarDoc effect touched it. Recorded where the pin layer is built, since that
        /// is the only place the entry's target and the pre-effect scene value are both in hand; read back by the
        /// final pass, the only place with a complete view of every layer any tool has added, to decide whether the
        /// pin's off state must hand the shape back to rest itself.</summary>
        public sealed class PinBinding
        {
            public string LayerName;
            public SkinnedMeshRenderer Renderer;
            public string Shape;
            public float RestValue;
        }

        public readonly List<PinBinding> Bindings = new List<PinBinding>();

        /// <summary>One material property channel a trigger's inactive state (a Hold trigger's Off, a Fade
        /// trigger's Far) leaves to the final pass: the layer, the renderer, the animated property name such as
        /// "material._Glow" or "material._Color.r", and the value it rests at. The final pass writes it into that
        /// state only when no other layer owns the binding, by the same rule a pinned shape is handed back by.</summary>
        public sealed class PropertyRest
        {
            public string LayerName;
            public Renderer Renderer;
            public string Property;
            public float RestValue;
        }

        public readonly List<PropertyRest> PropertyRests = new List<PropertyRest>();

        /// <summary>
        /// One curve a shape set rule wrote: in a toggle's or a Hold trigger's pin state, a Fade contact's Near child,
        /// or a blend shape trigger's driven state or child. Named by layer, state and child, because the final pass
        /// works on virtual objects rebuilt from the committed controller; HeldShapePruner reads it back there.
        /// </summary>
        public sealed class HeldCurve
        {
            public MenuEntry Entry;
            public SkinnedMeshRenderer Renderer;
            public string Shape;
            public string LayerName;
            public string StateName;
            /// <summary>Which of the layer's states named StateName holds the curve, counted from 0, for two states that share a name.</summary>
            public int StateOccurrence;
            /// <summary>The child of the state's blend tree that holds the curve, or -1 when the state's motion is the clip itself.</summary>
            public int Child = -1;
            public ShapeSetValue Value;
            /// <summary>The shape's weight before any AvatarDoc effect touched it (BaseState.WeightOf): what a Default rule holds it at.</summary>
            public float DefaultWeight;
            /// <summary>A shape the descriptor blinks or speaks with, held because the rule's Hold blink or Hold visemes switch is on.</summary>
            public bool Tracked;
        }

        public readonly List<HeldCurve> HeldCurves = new List<HeldCurve>();
    }
}
#endif
