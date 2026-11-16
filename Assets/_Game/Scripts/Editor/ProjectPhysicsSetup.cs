using UnityEditor;
using UnityEngine;
using ApexBrake.Track;

namespace ApexBrake.Editor
{
    /// <summary>Road / Ground / Car katman adlarını TagManager'a yazar (derlemeye girer). Editor açılışında denetlenir.</summary>
    [InitializeOnLoad]
    public static class ProjectPhysicsSetup
    {
        static ProjectPhysicsSetup() => EditorApplication.delayCall += () => EnsureLayers(false);

        [MenuItem("Apex Brake/Fizik/Katmanlari Kur")]
        public static void EnsureLayersMenu() => EnsureLayers(true);

        public static bool EnsureLayers(bool log)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return false;
            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");
            bool changed = false;
            changed |= Set(layers, TrackLayers.Road, TrackLayers.RoadName);
            changed |= Set(layers, TrackLayers.Ground, TrackLayers.GroundName);
            changed |= Set(layers, TrackLayers.Car, TrackLayers.CarName);
            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }
            if (log) Debug.Log($"[ProjectPhysicsSetup] Katmanlar: 8={LayerMask.LayerToName(8)} 9={LayerMask.LayerToName(9)} 10={LayerMask.LayerToName(10)} (degisti: {changed})");
            return changed;
        }

        static bool Set(SerializedProperty layers, int index, string name)
        {
            var p = layers.GetArrayElementAtIndex(index);
            if (p.stringValue == name) return false;
            if (!string.IsNullOrEmpty(p.stringValue))
            {
                Debug.LogError($"[ProjectPhysicsSetup] Katman {index} zaten '{p.stringValue}' olarak kullaniliyor; '{name}' yazilmadi.");
                return false;
            }
            p.stringValue = name;
            return true;
        }
    }
}
