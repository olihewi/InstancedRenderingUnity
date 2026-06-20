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
        [SerializeField] private Mesh m_DefaultInstanceMesh;
        [SerializeField] private Material m_DefaultInstanceMaterial;
        public bool SaveInstanceAssetsToScenePath => m_SaveInstanceAssetsToScenePath;
        public string InstanceAssetsFilePath => m_InstanceAssetsFilePath;
        public ScatteringBrush DefaultScatteringBrush => m_DefaultScatteringBrush;
        public Mesh DefaultInstanceMesh => m_DefaultInstanceMesh;
        public Material DefaultInstanceMaterial => m_DefaultInstanceMaterial;

    #if UNITY_EDITOR
        public const string k_Path = "Assets/Settings/InstancedRendering.asset";
        internal static InstancedRenderingConfiguration GetOrCreateSettings_Editor()
        {
            var settings = AssetDatabase.LoadAssetAtPath<InstancedRenderingConfiguration>(k_Path);
            if (settings == null)
            {
                settings = CreateInstance<InstancedRenderingConfiguration>();
                // ReSharper disable once PossibleNullReferenceException
                var packagePath = Path.GetDirectoryName(Path.GetDirectoryName(AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(settings)).TrimEnd('/')));
                settings.m_DefaultScatteringBrush = AssetDatabase.LoadAssetAtPath<ScatteringBrush>(Path.Combine(packagePath, "Brushes/Default Brush.asset"));
                settings.m_DefaultInstanceMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                settings.m_DefaultInstanceMaterial = AssetDatabase.LoadAssetAtPath<Material>(Path.Combine(packagePath, "Samples/InstancedRenderingSample.shadergraph"));
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
                    var saveInstanceAssetsToScenePath = settings.FindProperty(nameof(m_SaveInstanceAssetsToScenePath));
                    EditorGUILayout.PropertyField(saveInstanceAssetsToScenePath, new GUIContent("Save Instance Assets to Scene Pathr"));
                    if (!saveInstanceAssetsToScenePath.boolValue) EditorGUILayout.PropertyField(settings.FindProperty(nameof(m_InstanceAssetsFilePath)), new GUIContent("Instance Assets Scene Path"));
                    EditorGUILayout.PropertyField(settings.FindProperty(nameof(m_DefaultScatteringBrush)));
                    EditorGUILayout.PropertyField(settings.FindProperty(nameof(m_DefaultInstanceMesh)));
                    EditorGUILayout.PropertyField(settings.FindProperty(nameof(m_DefaultInstanceMaterial)));
                    settings.ApplyModifiedPropertiesWithoutUndo();
                },
                keywords = new HashSet<string>(new[] { "Instance Assets", "Rendering", "Renderer", "Instanced", "File Path" })
            };
            return provider;
        }
    #endif
    }
}