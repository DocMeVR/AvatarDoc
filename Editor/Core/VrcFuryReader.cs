#if DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Reads VRCFury components through SerializedObject by field name only. No VRCFury type is referenced anywhere
    /// in this project; a component is recognised by its runtime type name and its managed reference's full type name.
    /// </summary>
    public static class VrcFuryReader
    {
        public const string ToggleTypeName = "VF.Model.Feature.Toggle";
        public const string FullControllerTypeName = "VF.Model.Feature.FullController";

        public static bool IsVrcFury(Component c) => c != null && c.GetType().FullName == "VF.Model.VRCFury";

        /// <summary>VRCFury components found per avatar root. Walking every component of an avatar is the expensive part of reading them, and three call sites want the same list.</summary>
        static readonly Dictionary<Transform, List<Component>> componentCache = new Dictionary<Transform, List<Component>>();

        public static IEnumerable<Component> Components(Transform avatarRoot)
        {
            if (avatarRoot == null) return Enumerable.Empty<Component>();
            if (componentCache.TryGetValue(avatarRoot, out var cached)) return cached.Where(c => c != null);
            var found = avatarRoot.GetComponentsInChildren<Component>(true).Where(IsVrcFury).ToList();
            componentCache[avatarRoot] = found;
            return found;
        }

        /// <summary>Forgets what was read for this avatar, or for every avatar when root is null. Called wherever the plan cache drops a plan, so the two never disagree.</summary>
        public static void ClearCache(Transform avatarRoot = null)
        {
            if (avatarRoot == null) componentCache.Clear();
            else componentCache.Remove(avatarRoot);
        }

        /// <summary>The managed reference field that holds the one feature a VRCFury component carries, or null.</summary>
        public static SerializedProperty Content(SerializedObject so) => so.FindProperty("content");

        /// <summary>True when the managed reference held by content is the given VRCFury feature type, matched by the suffix of its full type name (the format is "&lt;assembly&gt; &lt;full type name&gt;").</summary>
        public static bool IsFeature(SerializedProperty content, string typeName)
        {
            if (content == null || string.IsNullOrEmpty(content.managedReferenceFullTypename)) return false;
            return content.managedReferenceFullTypename.EndsWith(typeName, StringComparison.Ordinal);
        }

        /// <summary>Splits a VRCFury toggle's menu path on unescaped slashes. "\/" is a literal slash inside a segment.</summary>
        public static List<string> SplitMenuPath(string name)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(name)) return result;
            var current = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (name[i] == '\\' && i + 1 < name.Length && name[i + 1] == '/')
                {
                    current.Append('/');
                    i++;
                }
                else if (name[i] == '/')
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(name[i]);
                }
            }
            result.Add(current.ToString());
            return result;
        }

        /// <summary>The global parameter name of every VRCFury toggle that drives one, for the parameter pickers.</summary>
        public static IEnumerable<string> GlobalParameters(Transform avatarRoot)
        {
            foreach (var component in Components(avatarRoot))
            {
                string found = null;
                using (var so = new SerializedObject(component))
                {
                    var content = Content(so);
                    if (!IsFeature(content, ToggleTypeName)) continue;
                    var useGlobal = content.FindPropertyRelative("useGlobalParam");
                    var globalParam = content.FindPropertyRelative("globalParam");
                    if (useGlobal == null || !useGlobal.boolValue) continue;
                    if (globalParam == null || string.IsNullOrEmpty(globalParam.stringValue)) continue;
                    found = globalParam.stringValue;
                }
                yield return found;
            }
        }

        /// <summary>
        /// Parameter names a Full Controller may read: the ones it lists as global, and every parameter of every
        /// controller it merges. A Full Controller merges its controllers late in the build, long after AvatarDoctor
        /// has decided what to free, and it can be set to treat every unsynced name as global or to list "*", so the
        /// whole set counts as read. Over keeping costs a few bits; freeing a name it reads breaks the avatar.
        /// </summary>
        public static IEnumerable<string> FullControllerParameters(Transform avatarRoot)
        {
            foreach (var component in Components(avatarRoot))
            {
                var found = new List<string>();
                using (var so = new SerializedObject(component))
                {
                    var content = Content(so);
                    if (!IsFeature(content, FullControllerTypeName)) continue;
                    var globals = content.FindPropertyRelative("globalParams");
                    if (globals != null && globals.isArray)
                        for (int i = 0; i < globals.arraySize; i++)
                            found.Add(globals.GetArrayElementAtIndex(i).stringValue);
                    var controllers = content.FindPropertyRelative("controllers");
                    if (controllers != null && controllers.isArray)
                        for (int i = 0; i < controllers.arraySize; i++)
                        {
                            var objRef = controllers.GetArrayElementAtIndex(i).FindPropertyRelative("controller")?.FindPropertyRelative("objRef");
                            // An override controller is followed to the controller it overrides, the same way
                            // MergeBuilder follows one: the parameters live there.
                            var merged = MergeBuilder.Resolve(objRef?.objectReferenceValue as RuntimeAnimatorController);
                            if (merged == null) continue;
                            foreach (var parameter in merged.parameters) found.Add(parameter.name);
                        }
                }
                foreach (var name in found)
                    if (!string.IsNullOrWhiteSpace(name)) yield return name;
            }
        }

        /// <summary>
        /// The parameters of the parameters assets Full Controllers merge that keep their own name in the build, as
        /// VRCFury decides it: a name its global list keeps (the name itself, or a prefix ending in "*"; a "!" in front
        /// of one takes it back), and with GoGo Loco every name under "Go/". VRCFury gives every other name one of its
        /// own, so nothing else on the avatar can read it by the name in the asset.
        /// </summary>
        public static List<VRCExpressionParameters.Parameter> FullControllerGlobalParameters(Transform avatarRoot)
        {
            var result = new List<VRCExpressionParameters.Parameter>();
            foreach (var component in Components(avatarRoot))
                using (var so = new SerializedObject(component))
                {
                    var content = Content(so);
                    if (!IsFeature(content, FullControllerTypeName)) continue;
                    var globals = new List<string>();
                    var list = content.FindPropertyRelative("globalParams");
                    if (list != null && list.isArray)
                        for (int i = 0; i < list.arraySize; i++) globals.Add(list.GetArrayElementAtIndex(i).stringValue ?? "");
                    var merged = new List<VRCExpressionParameters.Parameter>();
                    var prms = content.FindPropertyRelative("prms");
                    if (prms != null && prms.isArray)
                        for (int i = 0; i < prms.arraySize; i++)
                        {
                            var objRef = prms.GetArrayElementAtIndex(i).FindPropertyRelative("parameters")?.FindPropertyRelative("objRef");
                            if (objRef?.objectReferenceValue is VRCExpressionParameters asset && asset.parameters != null)
                                merged.AddRange(asset.parameters.Where(p => p != null && !string.IsNullOrEmpty(p.name)));
                        }
                    bool gogo = merged.Any(p => p.name == "Go/Locomotion") && MergesABaseController(content);
                    foreach (var parameter in merged)
                    {
                        if (gogo)
                            result.Add(parameter.name.StartsWith("Go/", StringComparison.Ordinal) ? parameter : new VRCExpressionParameters.Parameter
                            {
                                name = "Go/" + parameter.name, valueType = parameter.valueType, saved = parameter.saved, networkSynced = parameter.networkSynced, defaultValue = parameter.defaultValue,
                            });
                        else if (KeptGlobal(parameter.name, globals)) result.Add(parameter);
                    }
                }
            return result;
        }

        /// <summary>Whether a Full Controller's global list keeps a name, matched the way VRCFury matches it: the last plain match keeps it, any "!" match takes it back.</summary>
        static bool KeptGlobal(string name, List<string> globals)
        {
            bool kept = false;
            foreach (var entry in globals)
            {
                var global = entry ?? "";
                bool negative = global.StartsWith("!", StringComparison.Ordinal);
                if (negative) global = global.Substring(1);
                bool wildcard = global.EndsWith("*", StringComparison.Ordinal);
                if (wildcard) global = global.Substring(0, global.Length - 1);
                if (name != global && !(wildcard && name.StartsWith(global, StringComparison.Ordinal))) continue;
                if (negative) return false;
                kept = true;
            }
            return kept;
        }

        static bool MergesABaseController(SerializedProperty content)
        {
            var controllers = content.FindPropertyRelative("controllers");
            if (controllers == null || !controllers.isArray) return false;
            for (int i = 0; i < controllers.arraySize; i++)
            {
                var type = controllers.GetArrayElementAtIndex(i).FindPropertyRelative("type");
                if (type != null && type.intValue == (int)VRCAvatarDescriptor.AnimLayerType.Base) return true;
            }
            return false;
        }

        public const string ObjectToggleActionTypeName = "VF.Model.StateAction.ObjectToggleAction";
        public const string BlendShapeActionTypeName = "VF.Model.StateAction.BlendShapeAction";
        public const string MaterialActionTypeName = "VF.Model.StateAction.MaterialAction";
        public const string MaterialPropertyActionTypeName = "VF.Model.StateAction.MaterialPropertyAction";
        public const string AnimationClipActionTypeName = "VF.Model.StateAction.AnimationClipAction";

        public enum VrcFuryObjectToggleMode { TurnOn, TurnOff, Toggle }
        public enum VrcFuryMaterialPropertyType { Float, Color, Vector, St, LegacyAuto }
        public enum VrcFuryActionKind { ObjectToggle, BlendShape, Material, MaterialProperty, AnimationClip, Other }

        /// <summary>One action of a VRCFury toggle's state, read by field name into plain values.</summary>
        public sealed class VrcFuryAction
        {
            public VrcFuryActionKind Kind;
            /// <summary>Short type name, set only when Kind is Other, for the unsupported action note.</summary>
            public string TypeName = "";

            public GameObject ObjectToggleTarget;
            public VrcFuryObjectToggleMode ObjectToggleMode;

            public Renderer BlendShapeRenderer;
            public string BlendShapeName = "";
            public float BlendShapeValue = 100f;
            public bool BlendShapeAllRenderers = true;

            public Renderer MaterialRenderer;
            public int MaterialIndex;
            public Material MaterialValue;

            public GameObject MaterialPropertyTargetObject;
            public bool MaterialPropertyAffectAllMeshes;
            public string MaterialPropertyName = "";
            public VrcFuryMaterialPropertyType MaterialPropertyKind;
            public float MaterialPropertyValue;
            public Vector4 MaterialPropertyVector;
            public Color MaterialPropertyColor = Color.white;

            public AnimationClip AnimationClip;
        }

        /// <summary>A VRCFury toggle's fields and actions, read by field name into plain values.</summary>
        public sealed class VrcFuryToggle
        {
            public string Name = "";
            public bool Saved;
            public bool DefaultOn;
            public bool Slider;
            public float DefaultSliderValue;
            public List<string> ExclusiveTags = new List<string>();
            public Texture2D Icon;
            public string GlobalParameter = "";
            public bool HasTransition;
            public bool SecurityEnabled;
            public bool SeparateLocal;
            public bool HoldButton;
            public bool InvertRestLogic;
            public List<VrcFuryAction> Actions = new List<VrcFuryAction>();
        }

        /// <summary>Reads a VRCFury toggle's fields and actions by name. Null when the component does not carry a Toggle feature.</summary>
        public static VrcFuryToggle ReadToggle(Component c)
        {
            using (var so = new SerializedObject(c))
                return ReadToggle(Content(so));
        }

        static VrcFuryToggle ReadToggle(SerializedProperty content)
        {
            if (!IsFeature(content, ToggleTypeName)) return null;

            var toggle = new VrcFuryToggle
            {
                Name = content.FindPropertyRelative("name")?.stringValue ?? "",
                Saved = content.FindPropertyRelative("saved")?.boolValue ?? false,
                DefaultOn = content.FindPropertyRelative("defaultOn")?.boolValue ?? false,
                Slider = content.FindPropertyRelative("slider")?.boolValue ?? false,
                DefaultSliderValue = content.FindPropertyRelative("defaultSliderValue")?.floatValue ?? 0f,
                HasTransition = content.FindPropertyRelative("hasTransition")?.boolValue ?? false,
                SecurityEnabled = content.FindPropertyRelative("securityEnabled")?.boolValue ?? false,
                SeparateLocal = content.FindPropertyRelative("separateLocal")?.boolValue ?? false,
                HoldButton = content.FindPropertyRelative("holdButton")?.boolValue ?? false,
                InvertRestLogic = content.FindPropertyRelative("invertRestLogic")?.boolValue ?? false,
            };

            var enableExclusiveTag = content.FindPropertyRelative("enableExclusiveTag");
            if (enableExclusiveTag != null && enableExclusiveTag.boolValue)
            {
                var raw = content.FindPropertyRelative("exclusiveTag")?.stringValue ?? "";
                toggle.ExclusiveTags = raw.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            }

            var enableIcon = content.FindPropertyRelative("enableIcon");
            if (enableIcon != null && enableIcon.boolValue)
                toggle.Icon = ResolveGuidReference<Texture2D>(content.FindPropertyRelative("icon"));

            var useGlobalParam = content.FindPropertyRelative("useGlobalParam");
            if (useGlobalParam != null && useGlobalParam.boolValue)
                toggle.GlobalParameter = content.FindPropertyRelative("globalParam")?.stringValue ?? "";

            var actions = content.FindPropertyRelative("state")?.FindPropertyRelative("actions");
            if (actions != null)
                for (int i = 0; i < actions.arraySize; i++)
                    toggle.Actions.Add(ReadAction(actions.GetArrayElementAtIndex(i)));

            return toggle;
        }

        static VrcFuryAction ReadAction(SerializedProperty element)
        {
            if (IsFeature(element, ObjectToggleActionTypeName))
            {
                return new VrcFuryAction
                {
                    Kind = VrcFuryActionKind.ObjectToggle,
                    ObjectToggleTarget = element.FindPropertyRelative("obj")?.objectReferenceValue as GameObject,
                    ObjectToggleMode = (VrcFuryObjectToggleMode)(element.FindPropertyRelative("mode")?.enumValueIndex ?? 0),
                };
            }
            if (IsFeature(element, BlendShapeActionTypeName))
            {
                return new VrcFuryAction
                {
                    Kind = VrcFuryActionKind.BlendShape,
                    BlendShapeRenderer = element.FindPropertyRelative("renderer")?.objectReferenceValue as Renderer,
                    BlendShapeName = element.FindPropertyRelative("blendShape")?.stringValue ?? "",
                    BlendShapeValue = element.FindPropertyRelative("blendShapeValue")?.floatValue ?? 100f,
                    BlendShapeAllRenderers = element.FindPropertyRelative("allRenderers")?.boolValue ?? true,
                };
            }
            if (IsFeature(element, MaterialActionTypeName))
            {
                var renderer = element.FindPropertyRelative("renderer")?.objectReferenceValue as Renderer;
                if (renderer == null)
                {
                    var obsoleteObj = element.FindPropertyRelative("obj")?.objectReferenceValue as GameObject;
                    if (obsoleteObj != null) renderer = obsoleteObj.GetComponent<Renderer>();
                }
                return new VrcFuryAction
                {
                    Kind = VrcFuryActionKind.Material,
                    MaterialRenderer = renderer,
                    MaterialIndex = element.FindPropertyRelative("materialIndex")?.intValue ?? 0,
                    MaterialValue = ResolveGuidReference<Material>(element.FindPropertyRelative("mat")),
                };
            }
            if (IsFeature(element, MaterialPropertyActionTypeName))
            {
                var targetObject = element.FindPropertyRelative("renderer2")?.objectReferenceValue as GameObject;
                if (targetObject == null)
                {
                    var obsoleteRenderer = element.FindPropertyRelative("renderer")?.objectReferenceValue as Renderer;
                    if (obsoleteRenderer != null) targetObject = obsoleteRenderer.gameObject;
                }
                return new VrcFuryAction
                {
                    Kind = VrcFuryActionKind.MaterialProperty,
                    MaterialPropertyTargetObject = targetObject,
                    MaterialPropertyAffectAllMeshes = element.FindPropertyRelative("affectAllMeshes")?.boolValue ?? false,
                    MaterialPropertyName = element.FindPropertyRelative("propertyName")?.stringValue ?? "",
                    MaterialPropertyKind = (VrcFuryMaterialPropertyType)(element.FindPropertyRelative("propertyType")?.enumValueIndex ?? 0),
                    MaterialPropertyValue = element.FindPropertyRelative("value")?.floatValue ?? 0f,
                    MaterialPropertyVector = element.FindPropertyRelative("valueVector")?.vector4Value ?? Vector4.zero,
                    MaterialPropertyColor = element.FindPropertyRelative("valueColor")?.colorValue ?? Color.white,
                };
            }
            if (IsFeature(element, AnimationClipActionTypeName))
            {
                return new VrcFuryAction
                {
                    Kind = VrcFuryActionKind.AnimationClip,
                    AnimationClip = ResolveGuidReference<AnimationClip>(element.FindPropertyRelative("clip")),
                };
            }

            var full = element.managedReferenceFullTypename ?? "";
            var spaceIndex = full.LastIndexOf(' ');
            var typeName = spaceIndex >= 0 ? full.Substring(spaceIndex + 1) : full;
            var dotIndex = typeName.LastIndexOf('.');
            var shortName = dotIndex >= 0 ? typeName.Substring(dotIndex + 1) : typeName;
            return new VrcFuryAction { Kind = VrcFuryActionKind.Other, TypeName = shortName };
        }

        /// <summary>
        /// Resolves a GuidWrapper-shaped field (an "objRef" object reference with an "id" string fallback) without
        /// referencing VRCFury's wrapper type. The id, when objRef is empty, is the asset's guid optionally followed
        /// by ":fileId" and then "|fileName|objectName"; only the guid is needed to find the asset.
        /// </summary>
        static T ResolveGuidReference<T>(SerializedProperty wrapper) where T : UnityEngine.Object
        {
            if (wrapper == null) return null;
            var objRef = wrapper.FindPropertyRelative("objRef");
            if (objRef != null && objRef.objectReferenceValue is T direct) return direct;
            var idProp = wrapper.FindPropertyRelative("id");
            var id = idProp != null ? idProp.stringValue : null;
            if (string.IsNullOrEmpty(id)) return null;
            var guid = id.Split('|')[0].Split(':')[0];
            if (string.IsNullOrEmpty(guid)) return null;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }
    }
}
#endif
