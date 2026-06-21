using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.IO;
using Unity.Mathematics;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.SceneManagement;
#endif

namespace Marinade.InstancedRendering
{
    [Serializable]
    public struct InstancedRendererSettings
    {
        public Space transformSpace;
        
        public Mesh mesh;
        public Material material;
        public LightProbeUsage lightProbeUsage;
        public ShadowCastingMode shadowCastingMode;
        public ReflectionProbeUsage reflectionProbeUsage;
        public RenderingLayerMask renderingLayerMask;
        
        [FormerlySerializedAs("defaultBrush")] 
        public InstanceScatteringBrush brush;
        
        [Range(0.01F, 10F)] public float minScatterDistance;
    }

    [ExecuteAlways]
    [AddComponentMenu("Rendering/Instanced Renderer")]
    [Icon("Packages/com.marinade.instancedrendering/Editor/Icons/icon_InstancedRenderer.png")]
    public partial class InstancedRenderer : MonoBehaviour
    {
        [Serializable]
        public struct SpatialCell
        {
            public int Pointer;
            public int Count;
        }

        public const int INSTANCE_CELL_RATIO = 8;

        [SerializeField] private InstancedRendererSettings m_Settings;
        [SerializeField] private bool m_ModifiableInPlayMode = true;
        [SerializeField] private bool m_CompressSerializedData = false;
        [SerializeField] private bool m_UnloadSerializedDataOnStart = true;
        [SerializeField] private TextAsset m_SerializedData;

        private RenderParams _renderParams;
        private InstancingData _instanceData;
        
        private Space _currentTransformSpace;
        private LightProbeUsage _currentLightProbeUsage;

        private static readonly int _ObjectMatrix = Shader.PropertyToID("_ObjectMatrix");

        public InstancedRendererSettings Settings => m_Settings;
        public InstanceScatteringBrush Brush => m_Settings.brush;
        
    #region Unity Events

        private void Start()
        {
            _renderParams = new RenderParams(m_Settings.material)
            {
                entityId = gameObject.GetEntityId(),
                motionVectorMode = MotionVectorGenerationMode.Camera,
                matProps = new MaterialPropertyBlock(),
            };
            _currentTransformSpace = m_Settings.transformSpace;
            _currentLightProbeUsage = m_Settings.lightProbeUsage;
            LoadFromSerializedData();
        }
        private void OnEnable()
        {
        #if UNITY_EDITOR
            Undo.undoRedoEvent -= UndoRedoPerformed;
            Undo.undoRedoEvent += UndoRedoPerformed;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= SceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += SceneSaving;
        #endif
        }

        private void OnDisable()
        {
        #if UNITY_EDITOR
            Undo.undoRedoEvent -= UndoRedoPerformed;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= SceneSaving;
        #endif
        }

        private void OnDestroy()
        {
            if (_instanceData != null)
            {
                _instanceData.UnloadCPU();
                _instanceData.UnloadGPU();
            }
        #if UNITY_EDITOR
            Undo.undoRedoEvent -= UndoRedoPerformed;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= SceneSaving;
        #endif
        }

    #if UNITY_EDITOR
        private void Reset()
        {
            m_Settings = new InstancedRendererSettings()
            {
                transformSpace = Space.World,
                lightProbeUsage = LightProbeUsage.BlendProbes,
                shadowCastingMode = ShadowCastingMode.Off,
                reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox,
                renderingLayerMask = RenderingLayerMask.defaultRenderingLayerMask,
                minScatterDistance = 0.25F,
            };
            var config = InstancedRenderingConfiguration.GetOrCreateSettings_Editor();
            if (config != null)
            {
                m_Settings.brush = config.DefaultScatteringBrush;
                m_Settings.mesh = config.DefaultInstanceMesh;
                m_Settings.material = config.DefaultInstanceMaterial;
            }
            Clear();
        }
    #endif

        private void Update()
        {
            if (_instanceData != null)
            {
                if (m_Settings.transformSpace != _currentTransformSpace && _instanceData.Instances.IsCreated)
                {
                    _currentTransformSpace = m_Settings.transformSpace;
                }
                /*if (m_Settings.lightProbeUsage != _currentLightProbeUsage)
                {
                    _currentLightProbeUsage = m_Settings.lightProbeUsage;
                    UpdateLightProbes();
                }*/
                var targetSpatialHashingFactor = m_Settings.minScatterDistance;
                if (!Mathf.Approximately(targetSpatialHashingFactor, _instanceData.SpatialHashingFactor))
                {
                    _instanceData.SpatialHashingFactor = targetSpatialHashingFactor;
                    _instanceData.RebuildInstanceData();
                }
            }
            
            Render();
        }

    #endregion

        private void Render()
        {
            if (_instanceData == null || _instanceData.InstanceCount <= 0) return;
            if (_instanceData.IsCPUDirty || _instanceData.InstancesBuffer == null) _instanceData.UploadGPU(_renderParams.matProps);
            if (_instanceData.InstancesBuffer == null || m_Settings.mesh == null || m_Settings.material == null) return;
            _renderParams.material = m_Settings.material;
            _renderParams.layer = gameObject.layer;
            _renderParams.lightProbeUsage = m_Settings.lightProbeUsage;
            _renderParams.worldBounds = new Bounds(Vector3.zero, Vector3.one * 1000);

            
            if (m_Settings.transformSpace == Space.Self) _renderParams.matProps.SetMatrix(_ObjectMatrix, transform.localToWorldMatrix);
            else _renderParams.matProps.SetMatrix(_ObjectMatrix, Matrix4x4.identity);

            Graphics.RenderMeshPrimitives(_renderParams, m_Settings.mesh, 0, _instanceData.InstanceCount);
        }

    #region Modification

        public void AddInstance(Matrix4x4 instance, Space transformSpace = Space.World)
        {
            // Transform into this space
            if (transformSpace == Space.World && m_Settings.transformSpace == Space.Self)
                instance = transform.worldToLocalMatrix * instance;
            else if (transformSpace == Space.Self && m_Settings.transformSpace == Space.World)
                instance = transform.localToWorldMatrix * instance;

            _instanceData ??= new InstancingData(1F / (m_Settings.minScatterDistance * INSTANCE_CELL_RATIO));
            _instanceData.AddInstance(instance);
        }

        public void ReplaceInstance(int index, Matrix4x4 replacement, Space transformSpace = Space.World)
        {
            if (_instanceData == null || index < 0 || index >= _instanceData.InstanceCount) return;
            if (transformSpace == Space.World && m_Settings.transformSpace == Space.Self)
                replacement = transform.worldToLocalMatrix * replacement;
            else if (transformSpace == Space.Self && m_Settings.transformSpace == Space.World)
                replacement = transform.localToWorldMatrix * replacement;
            _instanceData.ReplaceInstance(index, replacement);
        }

        public void RemoveInstance(int index)
        {
            if (_instanceData == null || index < 0 || index >= _instanceData.InstanceCount) return;
            _instanceData.RemoveInstance(index);
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            if (_instanceData != null)
            {
                _instanceData.UnloadCPU();
                _instanceData.UnloadGPU();
            }
            _instanceData = null;
        #if UNITY_EDITOR
            Serialize_Editor();
        #endif
        }

    #endregion

    #region Serialization

        [ContextMenu("Load from Serialized Data")]
        public void LoadFromSerializedData()
        {
            if (m_SerializedData == null) return;
            using var ms = new MemoryStream(m_SerializedData.bytes);
            _instanceData ??= new InstancingData(1F / (m_Settings.minScatterDistance * INSTANCE_CELL_RATIO));
            _instanceData.LoadFromBytes(ms);
            if (m_UnloadSerializedDataOnStart && Application.isPlaying)
            {
                Destroy(m_SerializedData);
                m_SerializedData = null;
            }
        }
    #if UNITY_EDITOR
        public void Serialize_Editor()
        {
            if (Application.isPlaying) return;
            var group = Undo.GetCurrentGroup();
            // if instances null or empty
            Undo.RegisterCompleteObjectUndo(this, "Modified Instances");
            var fileName =
                $"instances_{gameObject.scene.name}_{name}_{GlobalObjectId.GetGlobalObjectIdSlow(this).targetObjectId}";
            if (m_SerializedData != null && m_SerializedData.name == fileName && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m_SerializedData)))
            {
                Undo.DestroyObjectImmediate(m_SerializedData);
            }
            if (_instanceData != null && _instanceData.InstanceCount > 0)
            {
                using var ms = new MemoryStream(_instanceData.GetByteSize());
                if (!_instanceData.WriteBytes(ms, m_CompressSerializedData)) return;
                m_SerializedData = new TextAsset(ms.ToArray()){name = fileName};
                Undo.RegisterCreatedObjectUndo(m_SerializedData, "Modified Instances");
            }
            Undo.CollapseUndoOperations(group);
        }
        public void SceneSaving(Scene _1, string _2)
        {
            if (m_SerializedData == null || !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m_SerializedData))) return;
            var config = InstancedRenderingConfiguration.GetOrCreateSettings_Editor();
            var path = config.SaveInstanceAssetsToScenePath ? gameObject.scene.path[..gameObject.scene.path.LastIndexOf('.')] : config.InstanceAssetsFilePath;
            path = Path.Combine(path, $"{m_SerializedData.name}.bytes");
            // ReSharper disable once AssignNullToNotNullAttribute
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, m_SerializedData.bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            m_SerializedData = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            EditorUtility.SetDirty(this);
        }

        private void UndoRedoPerformed(in UndoRedoInfo undo)
        {
            if (undo.undoName != "Modified Instances") return;
            OnEnable();
        }
    #endif

    #endregion
    }
}