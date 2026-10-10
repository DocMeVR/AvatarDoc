using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Builds real VRCFury components by reflection, so tests can exercise VrcFuryReader without this project
    /// referencing any VRCFury type. Guarded by Assume.That so a run without the VRCFury package installed skips
    /// the tests that need it instead of failing.
    /// </summary>
    public static class VrcFuryTestHelper
    {
        public static Type VrcFuryType => Type.GetType("VF.Model.VRCFury, VRCFury");
        public static Type ToggleType => Type.GetType("VF.Model.Feature.Toggle, VRCFury");
        public static Type FeatureType(string typeName) => Type.GetType(typeName + ", VRCFury");

        /// <summary>Adds a VRCFury component carrying one Toggle feature with the given menu path.</summary>
        public static Component AddToggle(GameObject go, string name)
        {
            var vrcFuryType = VrcFuryType;
            var toggleType = ToggleType;
            Assume.That(vrcFuryType, Is.Not.Null);
            Assume.That(toggleType, Is.Not.Null);

            var component = go.AddComponent(vrcFuryType);
            var content = Content(component, out var so);
            content.managedReferenceValue = Activator.CreateInstance(toggleType, true);
            so.ApplyModifiedPropertiesWithoutUndo();

            content = Content(component, out so);
            content.FindPropertyRelative("name").stringValue = name;
            so.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        /// <summary>Adds a VRCFury component carrying one feature of the given model type, built by reflection so no VRCFury type is referenced.</summary>
        public static Component AddFeature(GameObject go, string featureTypeName)
        {
            var vrcFuryType = VrcFuryType;
            var featureType = FeatureType(featureTypeName);
            Assume.That(vrcFuryType, Is.Not.Null);
            Assume.That(featureType, Is.Not.Null, featureTypeName);

            var component = go.AddComponent(vrcFuryType);
            var content = Content(component, out var so);
            content.managedReferenceValue = Activator.CreateInstance(featureType, true);
            so.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        public static SerializedProperty Content(Component c, out SerializedObject so)
        {
            so = new SerializedObject(c);
            return so.FindProperty("content");
        }

        /// <summary>Reads the toggle's managed instance, lets the caller change its fields by reflection, then writes it back.</summary>
        public static void ConfigureToggle(Component c, Action<object> configure) => ConfigureFeature(c, configure);

        /// <summary>The same for any feature model: read the managed instance, change it by reflection, write it back.</summary>
        public static void ConfigureFeature(Component c, Action<object> configure)
        {
            var content = Content(c, out var so);
            var instance = content.managedReferenceValue;
            configure(instance);
            content.managedReferenceValue = instance;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Reads a field of a reflected instance by name, searching base types too.</summary>
        public static object GetField(object instance, string field)
        {
            for (var t = instance.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f.GetValue(instance);
            }
            Assert.Fail("Field not found: " + field);
            return null;
        }

        /// <summary>Builds a VRCFury state action by reflection (its type is looked up by name so this project never references it) and appends it to the toggle's state.actions.</summary>
        public static void AddAction(Component c, string actionTypeName, Action<object> configure)
        {
            var actionType = Type.GetType(actionTypeName + ", VRCFury");
            Assume.That(actionType, Is.Not.Null, actionTypeName);
            var instance = Activator.CreateInstance(actionType, true);
            configure?.Invoke(instance);

            var content = Content(c, out var so);
            var actions = content.FindPropertyRelative("state").FindPropertyRelative("actions");
            int index = actions.arraySize;
            actions.InsertArrayElementAtIndex(index);
            actions.GetArrayElementAtIndex(index).managedReferenceValue = instance;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Builds a VRCFury guid wrapper (GuidTexture2d, GuidMaterial, GuidAnimationClip) around an asset, by reflection.</summary>
        public static object BuildGuidWrapper(string wrapperTypeName, UnityEngine.Object value)
        {
            var type = Type.GetType(wrapperTypeName + ", VRCFury");
            Assume.That(type, Is.Not.Null, wrapperTypeName);
            var instance = Activator.CreateInstance(type, true);
            SetField(instance, "objRef", value);
            return instance;
        }

        /// <summary>Sets a field of a reflected instance by name, searching base types too, converting a plain int into an enum field's type.</summary>
        public static void SetField(object instance, string field, object value)
        {
            for (var t = instance.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) continue;
                if (f.FieldType.IsEnum && value is int intValue) value = Enum.ToObject(f.FieldType, intValue);
                f.SetValue(instance, value);
                return;
            }
            Assert.Fail("Field not found: " + field);
        }
    }
}
