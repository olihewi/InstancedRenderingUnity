using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.Collections.Generic;
using System.IO;
#endif

namespace Marinade.InstancedRendering
{
    public class InstancedRenderingConfiguration : ScriptableObject
    {
        [SerializeField] private bool m_SaveInstanceAssetsToScenePath = true;
        [SerializeField] private string m_InstanceAssetsFilePath = "Assets/InstanceAssets";
        [SerializeField] private ScatteringBrush m_DefaultScatteringBrush;
        public bool SaveInstanceAssetsToScenePath => m_SaveInstanceAssetsToScenePath;
        public string InstanceAssetsFilePath => m_InstanceAssetsFilePath;
        public ScatteringBrush DefaultScatteringBrush => m_DefaultScatteringBrush;

    #if UNITY_EDITOR
        public const string k_Path = "Assets/Settings/InstancedRendering.asset";
        internal static InstancedRenderingConfiguration GetOrCreateSettings_Editor()
        {
            var settings = AssetDatabase.LoadAssetAtPath<InstancedRenderingConfiguration>(k_Path);
            if (settings == null)
            {
                settings = CreateInstance<InstancedRenderingConfiguration>();
                // ReSharper disable once PossibleNullReferenceException
                var path = Path.GetDirectoryName(Path.GetDirectoryName(AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(settings)).TrimEnd('/')));
                path = Path.Combine(path, "Brushes/Default Brush.asset");
                Debug.Log(path);
                settings.m_DefaultScatteringBrush = AssetDatabase.LoadAssetAtPath<ScatteringBrush>(path);
                AssetDatabase.CreateAsset(settings, k_Path);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }
        internal static SerializedObject GetSerializedSettings_Editor()
        {
            return new SerializedObject(GetOrCreateSettings_Editor());
        }
        
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider_Editor()
        {
            var provider = new SettingsProvider("Project/Instanced Rendering", SettingsScope.Project)
            {
                label = "Instanced Rendering",
                guiHandler = (searchContext) =>
                {
                    var settings = InstancedRenderingConfiguration.GetSerializedSettings_Editor();
                    var saveInstanceAssetsToScenePath = settings.FindProperty("m_SaveInstanceAssetsToScenePath");
                    EditorGUILayout.PropertyField(saveInstanceAssetsToScenePath, new GUIContent("Save Instance Assets to Scene Pathr"));
                    if (!saveInstanceAssetsToScenePath.boolValue) EditorGUILayout.PropertyField(settings.FindProperty("m_InstanceAssetsFilePath"), new GUIContent("Instance Assets Scene Path"));
                    EditorGUILayout.PropertyField(settings.FindProperty("m_DefaultScatteringBrush"));
                    settings.ApplyModifiedPropertiesWithoutUndo();
                },
                keywords = new HashSet<string>(new[] { "Instance Assets", "Rendering", "Renderer", "Instanced", "File Path" })
            };
            return provider;
        }
    #endif
    }
}