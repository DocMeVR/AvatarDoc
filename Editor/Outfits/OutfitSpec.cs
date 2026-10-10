#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    public enum OutfitMode { OneAtATime = 0, MixFreely = 1 }

    /// <summary>What the outfit set wizard turns into entries: a name, a mode and the outfits to build.</summary>
    public sealed class OutfitSpec
    {
        public string SetName = "";
        public OutfitMode Mode;
        public List<Outfit> Outfits = new List<Outfit>();
        /// <summary>One at a time only: pieces hidden while any outfit of the set is on.</summary>
        public List<GameObject> HiddenWhileAnyOutfit = new List<GameObject>();
        public int DefaultOutfit = 0;
        /// <summary>One at a time only: adds a toggle that shows nothing.</summary>
        public bool AddNoneOption;
    }

    /// <summary>One outfit of a set: its pieces and the shrink suggestions chosen for them.</summary>
    public sealed class Outfit
    {
        public string Name = "";
        public List<GameObject> Pieces = new List<GameObject>();
        public List<ShrinkSuggestion> Shrinks = new List<ShrinkSuggestion>();
    }

    /// <summary>A blend shape the suggester found on another mesh that may need to shrink while a piece is shown.</summary>
    public sealed class ShrinkSuggestion
    {
        public SkinnedMeshRenderer Renderer;
        public string Shape = "";
        public GameObject Piece;
        public bool Chosen;
        [Range(0f, 100f)] public float Weight = 100f;
    }
}
#endif
