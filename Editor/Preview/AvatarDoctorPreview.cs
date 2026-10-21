#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>NDMF scene preview: the effective state of every control and trigger applied to proxy renderers.</summary>
    public sealed class AvatarDoctorPreview : IRenderFilter
    {
        public static readonly TogglablePreviewNode Toggle = TogglablePreviewNode.Create(() => "AvatarDoc", "docme.avatar-doctor.preview", true);

        public bool CanEnableRenderers => true;

        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes()
        {
            yield return Toggle;
        }

        public bool IsEnabled(ComputeContext context) => context.Observe(Toggle.IsEnabled);

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            var groups = ImmutableList.CreateBuilder<RenderGroup>();
            foreach (var root in context.GetAvatarRoots()) groups.AddRange(GroupsFor(context, root));
            return groups.ToImmutable();
        }

        public static IEnumerable<RenderGroup> GroupsFor(ComputeContext context, GameObject root)
        {
            if (!context.ActiveInHierarchy(root)) yield break;
            var overrides = context.Observe(PreviewState.Overrides, v => v, (a, b) => ReferenceEquals(a, b));
            var enabled = new List<AvatarDoc>();
            foreach (var component in context.GetComponentsInChildren<AvatarDoc>(root, true))
            {
                context.Observe(component);
                if (component.enabled) enabled.Add(component);
            }
            // The model plans the parameters itself, so a change to a declaration that decides a control's default
            // state has to be observed here as well.
            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null)
            {
                context.Observe(descriptor);
                if (descriptor.expressionParameters != null) context.Observe(descriptor.expressionParameters);
                var fx = ParametersWriter.FxOf(descriptor);
                if (fx != null) context.Observe(fx);
            }
            var scene = new SceneReader
            {
                ActiveSelf = go => context.Observe(go, g => g.activeSelf),
                RendererEnabled = r => context.Observe(r, x => x.enabled),
                ShapeWeight = (r, i) => context.Observe(r, x => x.GetBlendShapeWeight(i)),
                Materials = r => context.Observe(r, x => x.sharedMaterials, (a, b) => a != null && b != null && a.SequenceEqual(b)),
                Renderers = t => context.GetComponentsInChildren<Renderer>(t.gameObject, true),
                WorldPosition = t => context.Observe(t, x => x.position),
                WorldRotation = t => context.Observe(t, x => x.rotation),
            };

            // The model reads sharedMesh, sharedMaterials.Length and the parent chain directly rather than through
            // SceneReader, so register those observations here or a mesh swap, slot count change or reparent goes unnoticed.
            foreach (var renderer in context.GetComponentsInChildren<Renderer>(root, true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                context.ObservePath(renderer.transform);
                context.Observe(renderer, r => r.sharedMaterials, (a, b) => a != null && b != null && a.SequenceEqual(b));
                if (renderer is SkinnedMeshRenderer skinned) context.Observe(skinned, s => s.sharedMesh);
            }
            // The shapes components of the outfit pieces: what they write depends on whether their own object is
            // on, so the path and the active flag are observed as well.
            var pieces = new List<AvatarDocShapes>();
            foreach (var component in context.GetComponentsInChildren<AvatarDocShapes>(root, true))
            {
                context.Observe(component);
                if (!component.enabled) continue;
                context.ObservePath(component.transform);
                context.Observe(component.gameObject, g => g.activeSelf);
                pieces.Add(component);
            }

            foreach (var component in enabled)
                foreach (var entry in component.entries)
                {
                    foreach (var target in entry.objects)
                    {
                        if (target.target == null) continue;
                        context.ObservePath(target.target.transform);
                        context.Observe(target.target, g => g.activeSelf);
                    }
                    foreach (var follow in entry.follows)
                    {
                        if (follow.target == null || follow.follows == null) continue;
                        context.ObservePath(follow.target.transform);
                        context.ObservePath(follow.follows);
                    }
                }

            foreach (var pair in PreviewModel.Compute(root.transform, enabled, overrides, scene, pieces))
                yield return RenderGroup.For(pair.Key).WithData(pair.Value, (a, b) => a.Equals(b));
        }

        public Task<IRenderFilterNode> Instantiate(RenderGroup group, IEnumerable<(Renderer, Renderer)> proxyPairs, ComputeContext context)
            => Task.FromResult<IRenderFilterNode>(new Node(group.GetData<RendererState>()));

        public sealed class Node : IRenderFilterNode
        {
            readonly RendererState state;
            Material[] clones;
            Material[] clonedFrom;
            /// <summary>What each frame writes into the copies: the copy, the name it draws the property with, and the value. Read once per set of copies.</summary>
            readonly List<(Material clone, string name, PropertyValueEntry value)> writes = new List<(Material, string, PropertyValueEntry)>();

            public Node(RendererState state) { this.state = state; }

            public RenderAspects WhatChanged => (state.Shapes.IsEmpty ? 0 : RenderAspects.Shapes) | (state.Materials.IsEmpty && state.Properties.IsEmpty ? 0 : RenderAspects.Material);

            public void OnFrame(Renderer original, Renderer proxy)
            {
                proxy.enabled = state.Visible;
                if (!state.Visible) return;
                // The proxy is a stand-in Unity swaps into the real scene for the preview only, so moving it here
                // shows where the object would land without ever touching the real object's own transform.
                if (state.HasFollow) proxy.transform.SetPositionAndRotation(state.FollowPosition, state.FollowRotation);
                if (proxy is SkinnedMeshRenderer skinned && !state.Shapes.IsEmpty && skinned.sharedMesh != null)
                    foreach (var shape in state.Shapes)
                        if (shape.Key >= 0 && shape.Key < skinned.sharedMesh.blendShapeCount) skinned.SetBlendShapeWeight(shape.Key, shape.Value);

                var materials = proxy.sharedMaterials;
                if (!state.Materials.IsEmpty)
                {
                    bool changed = false;
                    foreach (var slot in state.Materials)
                        if (slot.Key >= 0 && slot.Key < materials.Length && materials[slot.Key] != slot.Value) { materials[slot.Key] = slot.Value; changed = true; }
                    if (changed) { proxy.sharedMaterials = materials; materials = proxy.sharedMaterials; }
                }

                if (state.Properties.IsEmpty) return;
                EnsureClones(materials);
                foreach (var (clone, name, value) in writes) SetProperty(clone, name, value);
                proxy.sharedMaterials = clones;
            }

            /// <summary>Clones the proxy's current shared materials once, and again whenever a material slot swap changes what is at a slot, so a property target and a material target on the same renderer keep working together.</summary>
            void EnsureClones(Material[] currentMaterials)
            {
                if (clones != null && SameSource(currentMaterials)) return;
                DestroyClones();
                clonedFrom = currentMaterials;
                clones = new Material[currentMaterials.Length];
                for (int i = 0; i < currentMaterials.Length; i++)
                {
                    var source = currentMaterials[i];
                    clones[i] = source != null ? new Material(source) { hideFlags = HideFlags.HideAndDontSave } : null;
                    if (clones[i] == null) continue;
                    // The name is read from the material the copy was made from: a Poiyomi rename suffix can come from its name.
                    foreach (var property in state.Properties)
                    {
                        var name = PoiyomiLock.NameOn(source, property.Key);
                        if (name != null && clones[i].HasProperty(name)) writes.Add((clones[i], name, property.Value));
                    }
                }
            }

            bool SameSource(Material[] current)
            {
                if (clonedFrom == null || clonedFrom.Length != current.Length) return false;
                for (int i = 0; i < current.Length; i++)
                    if (clonedFrom[i] != current[i]) return false;
                return true;
            }

            void DestroyClones()
            {
                writes.Clear();
                if (clones == null) return;
                foreach (var clone in clones)
                    if (clone != null) Object.DestroyImmediate(clone);
                clones = null;
                clonedFrom = null;
            }

            static void SetProperty(Material material, string property, PropertyValueEntry value)
            {
                switch (value.Type)
                {
                    case PropertyValueType.Color: material.SetColor(property, value.Value); break;
                    case PropertyValueType.Vector: material.SetVector(property, value.Value); break;
                    default: material.SetFloat(property, value.Value.x); break;
                }
            }

            public void Dispose() => DestroyClones();
        }
    }
}
#endif
