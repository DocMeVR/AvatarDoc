#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    public enum ImportSource { VRCFury, ModularAvatar, FxLayer, MergeAnimator, MenuAsset, Shapes }

    /// <summary>The rows an AvatarDoc Shapes component gets on one outfit piece.</summary>
    public sealed class ShapesDraft
    {
        public GameObject Piece;
        public List<ShapeFollow> Follow = new List<ShapeFollow>();
        public List<ShapeWhileOn> WhileOn = new List<ShapeWhileOn>();

        public int RowCount => Follow.Count + WhileOn.Count;
    }

    /// <summary>Parameter declarations to take out of a component that other things still need, instead of destroying it.</summary>
    public sealed class ParameterTrim
    {
        public Component Component;
        public List<string> Names = new List<string>();
    }

    /// <summary>One item another tool would install at build time, offered for conversion into an AvatarDoc entry.</summary>
    public sealed class ImportCandidate
    {
        public ImportSource Source;
        /// <summary>Plain submenu names from the root down to, but not including, the drafted entry.</summary>
        public List<string> Path = new List<string>();
        public MenuEntry Draft;
        /// <summary>Set instead of Draft on the candidate that stands for a merged animator controller itself.</summary>
        public AnimatorMerge MergeDraft;
        /// <summary>Set instead of Draft on the candidate that stands for a finished menu asset taken over as it is.</summary>
        public MenuAssetInstall MenuAssetDraft;
        /// <summary>Set instead of Draft on the candidate that becomes an AvatarDoc Shapes component on an outfit piece.</summary>
        public ShapesDraft ShapesDraft;
        /// <summary>Parameter declarations that come over with this candidate, from the Modular Avatar Parameters component of the same set.</summary>
        public List<ParameterDeclaration> ParameterDrafts = new List<ParameterDeclaration>();
        /// <summary>The creator asked for this whole group to be taken over unchanged: no layer is converted, and the merged animator keeps every one of them.</summary>
        public bool KeepAsItIs;
        /// <summary>The parameter control this item becomes when its group is kept as it is. Set only where a real conversion was possible.</summary>
        public MenuEntry KeepDraft;
        /// <summary>The parameter the converted entry replaces, so a control of an installed menu asset that drives it can be left out.</summary>
        public string MergedParameter;
        /// <summary>Candidates with the same non empty group belong together: choosing one chooses them all.</summary>
        public string Group;
        /// <summary>Every candidate of this candidate's group, the same list on each of them. Null when it stands alone.</summary>
        public List<ImportCandidate> GroupMembers;
        /// <summary>The layer of a merged controller this entry replaces, so the merge can be told what to leave out once the entry really exists.</summary>
        public string MergedLayer;
        /// <summary>Declarations to take out of components the conversion cannot destroy outright.</summary>
        public List<ParameterTrim> Trims = new List<ParameterTrim>();
        /// <summary>Localised sentences for everything about the source that could not be carried over.</summary>
        public List<string> Notes = new List<string>();
        /// <summary>False: listed for visibility but cannot be ticked; Reason explains why.</summary>
        public bool Supported = true;
        public string Reason;
        public List<Component> OriginComponents = new List<Component>();
        /// <summary>Set only for an FX layer candidate: the layer's name.</summary>
        public string OriginLayer;
        /// <summary>Set only for an FX layer candidate: the path of the avatar's own menu control that drives it.</summary>
        public List<string> OriginControlPath = new List<string>();
        /// <summary>Set only for an FX layer candidate: which match OriginControlPath refers to, computed the same way MenuEditing computes it.</summary>
        public int OriginOccurrence;
    }
}
#endif
