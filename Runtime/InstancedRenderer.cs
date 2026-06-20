using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.IO;
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
            public ushort Pointer;
            public ushort Count;
        }

        public const int SPATIAL_HASH_TABLE_SIZE = 512;
        public const int INSTANCE_CELL_RATIO = 8;

        [SerializeField] private InstancedRendererSettings m_Settings;
        [SerializeField, HideInInspector] private Bounds m_Bounds;
        [SerializeField] private TextAsset m_SerializedData;
        [SerializeField, HideInInspector] private int m_SerializedVersion;
        [SerializeField] private int m_InstanceCount;

        private RenderParams _renderParams;
        private List<Instance> _instances;
        private SpatialCell[] _spatialHashTable;
        private List<ushort> _activeHashIndices;
        private float _spatialHashingFactor, _inverseSpatialHashingFactor;
        private bool _dirtyThisFrame = false;
        private ComputeBuffer _instancesBuffer;
        
        private Space _currentTransformSpace;
        private LightProbeUsage _currentLightProbeUsage;
        private float _currentMinScatterDistance;

        private static readonly int _PerInstanceData = Shader.PropertyToID("_PerInstanceData");
        private static readonly int _ObjectMatrix = Shader.PropertyToID("_ObjectMatrix");

        public InstancedRendererSettings Settings => m_Settings;
        public InstanceScatteringBrush Brush => m_Settings.brush;
        
    #region Unity Events
        
        private void OnEnable()
        {
            _renderParams = new RenderParams(m_Settings.material)
            {
                entityId = gameObject.GetEntityId(),
                motionVectorMode = MotionVectorGenerationMode.Camera,
                matProps = new MaterialPropertyBlock(),
            };
            _currentTransformSpace = m_Settings.transformSpace;
            _currentLightProbeUsage = m_Settings.lightProbeUsage;
            _currentMinScatterDistance = m_Settings.minScatterDistance;
            OnValidate();
            if (m_SerializedData != null)
            {
                using var ms = new MemoryStream(m_SerializedData.bytes);
                LoadFromBytes(ms);
            }
        #if UNITY_EDITOR
            Undo.undoRedoEvent -= UndoRedoPerformed;
            Undo.undoRedoEvent += UndoRedoPerformed;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= SceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += SceneSaving;
        #endif
        }

        private void OnDisable()
        {
            _instancesBuffer?.Release();
            _instancesBuffer = null;
        #if UNITY_EDITOR
            Undo.undoRedoEvent -= UndoRedoPerformed;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= SceneSaving;
        #endif
        }

        private void OnValidate()
        {
            _spatialHashingFactor = 1F / (m_Settings.minScatterDistance * INSTANCE_CELL_RATIO);
            _inverseSpatialHashingFactor = 1F / _spatialHashingFactor;
            m_InstanceCount = _instances != null ? _instances.Count : 0;
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
            if (m_Settings.transformSpace != _currentTransformSpace)
            {
                RebuildInstanceData();
                _currentTransformSpace = m_Settings.transformSpace;
            }
            if (m_Settings.lightProbeUsage != _currentLightProbeUsage)
            {
                _currentLightProbeUsage = m_Settings.lightProbeUsage;
                UpdateLightProbes();
            }
            if (!Mathf.Approximately(m_Settings.minScatterDistance, _currentMinScatterDistance))
            {
                _currentMinScatterDistance = m_Settings.minScatterDistance;
                RebuildInstanceData();
            }
            
            Render();
        }

    #endregion

        private void UpdateBuffers()
        {
            _dirtyThisFrame = false;
            if (_instances == null || _instances.Count == 0)
            {
                _instancesBuffer?.Release();
                _instancesBuffer = null;
                m_InstanceCount = 0;
                return;
            }
            m_InstanceCount = _instances.Count;
            if (_instancesBuffer == null || _instancesBuffer.count != _instances.Count)
            {
                _instancesBuffer?.Release();
                _instancesBuffer = new ComputeBuffer(_instances.Count, sizeof(float) * 20);
            }
            _instancesBuffer.SetData(_instances, 0, 0, _instances.Count);
            _renderParams.matProps.SetBuffer(_PerInstanceData, _instancesBuffer);
        }
        
        private void Render()
        {
            if (_instances == null) return;
            if (_dirtyThisFrame)
            {
                UpdateBuffers();
            }
            if (_instancesBuffer == null || m_Settings.mesh == null || m_Settings.material == null) return;
            _renderParams.material = m_Settings.material;
            _renderParams.layer = gameObject.layer;
            _renderParams.lightProbeUsage = m_Settings.lightProbeUsage;
            _renderParams.worldBounds = new Bounds(Vector3.zero, Vector3.one * 1000);

            
            if (m_Settings.transformSpace == Space.Self) _renderParams.matProps.SetMatrix(_ObjectMatrix, transform.localToWorldMatrix);
            else _renderParams.matProps.SetMatrix(_ObjectMatrix, Matrix4x4.identity);

            Graphics.RenderMeshPrimitives(_renderParams, m_Settings.mesh, 0, _instances.Count);
        }

    #region Modification

        public void AddInstance(Matrix4x4 instance, Space transformSpace = Space.World)
        {
            // Transform into this space
            if (transformSpace == Space.World && m_Settings.transformSpace == Space.Self)
                instance = transform.worldToLocalMatrix * instance;
            else if (transformSpace == Space.Self && m_Settings.transformSpace == Space.World)
                instance = transform.localToWorldMatrix * instance;

            if (_instances == null)
            {
                RebuildSpatialHashTable();
                _instances = new List<Instance>(64);
            }
            
            var position = instance.GetPosition();
            var spatialIndex = GetSpatialIndex(position);
            if (_instances.Count == 0) m_Bounds = new Bounds(position, Vector3.zero);
            else m_Bounds.Encapsulate(position);


            var instanceStruct = new Instance(instance);
            _instances.Add(instanceStruct);
            
            // If there are no other elements in this spatial hash, add it to the end of the instance list.
            if (_spatialHashTable[spatialIndex].Count == 0)
            {
                _spatialHashTable[spatialIndex].Pointer = (ushort)(_instances.Count - 1);
                _spatialHashTable[spatialIndex].Count = 1;
                _activeHashIndices.Add(spatialIndex);
                return;
            }
            
            // Else move subsequent hash cells along and update their pointers
            var activeIndex = _activeHashIndices.IndexOf(spatialIndex);
            var swapIndex = _instances.Count - 1;
            for (int i = _activeHashIndices.Count - 1; i > activeIndex; i--)
            {
                _instances[swapIndex] = _instances[swapIndex = _spatialHashTable[_activeHashIndices[i]].Pointer++];
            }
            _instances[swapIndex] = instanceStruct;
            _spatialHashTable[spatialIndex].Count++;
            _dirtyThisFrame = true;
        }

        public void ReplaceInstance(int index, Matrix4x4 replacement)
        {
            if (_instances == null || index < 0 || index >= _instances.Count) return;
            _instances[index] = new Instance(replacement);
            _dirtyThisFrame = true;
        }

        public void RemoveInstance(int index)
        {
            if (_instances == null || index < 0 || index >= _instances.Count) return;
            var spatialIndex = GetSpatialIndex(_instances[index].matrix.GetPosition());
            var activeIndex = _activeHashIndices.IndexOf(spatialIndex);
            if (activeIndex < 0)
            {
                Debug.LogError("InstancedRenderer data is broken, cannot remove instance!");
                return;
            }

            // Swap to back of spatial cell
            ref var modifiedCell = ref _spatialHashTable[spatialIndex];
            var swapIndex = modifiedCell.Pointer + modifiedCell.Count - 1;
            _instances[index] = _instances[swapIndex];
            
            // Move subsequent hash cells back and update their pointers
            for (int i = activeIndex + 1; i < _activeHashIndices.Count; i++)
            {
                ref var nextCell = ref _spatialHashTable[_activeHashIndices[i]];
                var lastIndexInNextCell = --nextCell.Pointer + nextCell.Count;
                _instances[swapIndex] = _instances[lastIndexInNextCell];
                swapIndex = lastIndexInNextCell;
            }
            
            // Remove from active hash indices if cell is empty
            if (--modifiedCell.Count <= 0)
            {
                _activeHashIndices.RemoveAt(activeIndex);
            }
            _instances.RemoveAt(_instances.Count - 1);
            _dirtyThisFrame = true;
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            _instances = null;
            RebuildSpatialHashTable();
            UpdateBuffers();
        #if UNITY_EDITOR
            Serialize_Editor();
        #endif
        }

        public void UpdateLightProbes()
        {
            if (_instances == null || _instances.Count == 0) return;
            
            var positions = new Vector3[_instances.Count];
            for (int i = _instances.Count - 1; i >= 0; --i)
            {
                positions[i] = _instances[i].matrix.GetPosition();
            }
            
            var lightProbes = new SphericalHarmonicsL2[_instances.Count];
            var occlusionProbes = new Vector4[_instances.Count];
            LightProbes.CalculateInterpolatedLightAndOcclusionProbes(positions, lightProbes, occlusionProbes);
            for (int i = _instances.Count - 1; i >= 0; --i)
            {
                var instance = _instances[i];
                Instance.LIGHT_PROBE_SAMPLE_VECTOR[0] = instance.matrix.rotation * Vector3.up;
                lightProbes[i].Evaluate(Instance.LIGHT_PROBE_SAMPLE_VECTOR, Instance.LIGHT_PROBE_EVALUATION_RESULT);
                instance.probeColor = Instance.LIGHT_PROBE_EVALUATION_RESULT[0];
                _instances[i] = instance;
            }
            _dirtyThisFrame = true;
        }
        
        public void RebuildInstanceData()
        {
            var instances = _instances;
            var instanceCount = instances.Count;
            Clear();
            _instances = new List<Instance>(instanceCount);
            for (int i = 0; i < instanceCount; ++i)
            {
                AddInstance(instances[i].matrix, _currentTransformSpace);
            }
        }

    #endregion
        
    #region Spatial Hashing

        public Vector3Int GetHashCell(Vector3 position)
        {
            return Vector3Int.FloorToInt(position * _spatialHashingFactor);
        }
        public ushort GetSpatialIndex(Vector3Int hashCell)
        {
            var hashCode = (hashCell.x * 92837111) ^ (hashCell.y * 689287499) ^ (hashCell.z * 283923481);
            return (ushort)(Mathf.Abs(hashCode) % SPATIAL_HASH_TABLE_SIZE);
        }
        public ushort GetSpatialIndex(Vector3 position)
        {
            return GetSpatialIndex(GetHashCell(position));
        }

        private void RebuildSpatialHashTable(int activeHashIndicesCapacity = 64)
        {
            _spatialHashTable = new SpatialCell[SPATIAL_HASH_TABLE_SIZE];
            _activeHashIndices = new List<ushort>(activeHashIndicesCapacity);
            ushort prevSpatialIndex = ushort.MaxValue;
            if (_instances == null) return;
            for (int i = 0; i < _instances.Count; i++)
            {
                var spatialIndex = GetSpatialIndex(_instances[i].matrix.GetPosition());
                if (prevSpatialIndex != spatialIndex)
                {
                    _spatialHashTable[spatialIndex].Pointer = (ushort)i;
                    _activeHashIndices.Add(spatialIndex);
                    prevSpatialIndex = spatialIndex;
                }
                _spatialHashTable[spatialIndex].Count++;
            }
        }

    #endregion

    #region Query

        public int GetFirstOverlappingInstance(Vector3 position, float radius, Space transformSpace = Space.World)
        {
            if (_instances == null) return -1;
            // Transform into this space
            if (transformSpace == Space.World && m_Settings.transformSpace == Space.Self)
                position = transform.InverseTransformPoint(position);
            else if (transformSpace == Space.Self && m_Settings.transformSpace == Space.World)
                position = transform.TransformPoint(position);
            
            var hashCell = GetHashCell(position);
            var radiusSqr = radius * radius;

            int result;
            var maxCellRadius = Mathf.Max(1, Mathf.CeilToInt(radius / _currentMinScatterDistance) + 1);
            for (int x = 0; x <= maxCellRadius; x++)
            {
                var sphereRelative = x / (float)maxCellRadius;
                var sphereSlice = Mathf.CeilToInt(Mathf.Sqrt(1F - sphereRelative * sphereRelative));
                for (int y = 0; y <= sphereSlice; y++)
                {
                    for (int z = 0; z <= sphereSlice; z++)
                    {
                        result = GetFirstOverlappingInstanceInCell(hashCell + new Vector3Int(x, y, z), position,
                            radiusSqr);
                        if (result >= 0) return result;
                        if (x != 0)
                        {
                            result = GetFirstOverlappingInstanceInCell(hashCell + new Vector3Int(-x, y, z), position,
                                radiusSqr);
                            if (result >= 0) return result;
                            if (y != 0)
                            {
                                result = GetFirstOverlappingInstanceInCell(hashCell + new Vector3Int(-x, -y, z), position,
                                    radiusSqr);
                                if (result >= 0) return result;
                                if (z != 0)
                                {
                                    result = GetFirstOverlappingInstanceInCell(hashCell + new Vector3Int(-x, -y, -z), position,
                                        radiusSqr);
                                    if (result >= 0) return result;
                                }
                            }
                            else if (z != 0)
                            {
                                result = GetFirstOverlappingInstanceInCell(hashCell + new Vector3Int(-x, y, -z), position,
                                    radiusSqr);
                                if (result >= 0) return result;
                            }
                        }
                        else if (y != 0)
                        {
                            result = GetFirstOverlappingInstanceInCell(hashCell + new Vector3Int(x, -y, z), position,
                                radiusSqr);
                            if (result >= 0) return result;
                            if (z != 0)
                            {
                                result = GetFirstOverlappingInstanceInCell(hashCell + new Vector3Int(x, -y, -z), position,
                                    radiusSqr);
                                if (result >= 0) return result;
                            }
                        }
                        else if (z != 0)
                        {
                            result = GetFirstOverlappingInstanceInCell(hashCell + new Vector3Int(x, y, -z), position,
                                radiusSqr);
                            if (result >= 0) return result;
                        }
                    }
                }
            }
            return -1;
        }

        public int GetFirstOverlappingInstanceInCell(Vector3Int cell, Vector3 position, float radiusSqr)
        {
            var spatialIndex = GetSpatialIndex(cell);
            var ptrMax = _spatialHashTable[spatialIndex].Pointer + _spatialHashTable[spatialIndex].Count;
            for (int instanceIdx = _spatialHashTable[spatialIndex].Pointer; instanceIdx < ptrMax; instanceIdx++)
            {
                if ((_instances[instanceIdx].matrix.GetPosition() - position).sqrMagnitude < radiusSqr) return instanceIdx;
            }
            return -1;
        }

    #endregion

    #region Serialization

        public const int CURRENT_SERIALIZED_VERSION = 1;
        public int GetByteSize() => sizeof(int) * 3 + sizeof(float) * 6 + (_instances != null ? _instances.Count * sizeof(float) * 16 : 0);
        public bool WriteBytes(Stream stream)
        {
            try
            {
                using var bw = new BinaryWriter(stream);
                bw.Write(CURRENT_SERIALIZED_VERSION);
                bw.Write(_activeHashIndices.Count);
                bw.Write(m_Bounds.center.x);
                bw.Write(m_Bounds.center.y);
                bw.Write(m_Bounds.center.z);
                bw.Write(m_Bounds.size.x);
                bw.Write(m_Bounds.size.y);
                bw.Write(m_Bounds.size.z);
                if (_instances == null)
                {
                    bw.Write(0);
                    return true;
                }
                var instanceCount = _instances.Count;
                bw.Write(instanceCount);
                for (int i = 0; i < instanceCount; ++i)
                {
                    var m = _instances[i].matrix;
                    bw.Write(m.m00);
                    bw.Write(m.m10);
                    bw.Write(m.m20);
                    bw.Write(m.m30);
                    bw.Write(m.m01);
                    bw.Write(m.m11);
                    bw.Write(m.m21);
                    bw.Write(m.m31);
                    bw.Write(m.m02);
                    bw.Write(m.m12);
                    bw.Write(m.m22);
                    bw.Write(m.m32);
                    bw.Write(m.m03);
                    bw.Write(m.m13);
                    bw.Write(m.m23);
                    bw.Write(m.m33);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                return false;
            }
        }

        public bool LoadFromBytes(Stream stream)
        {
            try
            {
                using var br = new BinaryReader(stream);
                int serializedVersion = br.ReadInt32();
                int activeHashIndicesCount = br.ReadInt32();
                m_Bounds = new Bounds(new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                    new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                int instanceCount = br.ReadInt32();
                _instances = new List<Instance>(instanceCount);
                for (int i = 0; i < instanceCount; ++i)
                {
                    _instances.Add(new Instance{matrix = new Matrix4x4(
                        new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                        new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                        new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                        new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()))});
                }
                RebuildSpatialHashTable(activeHashIndicesCount);
                UpdateLightProbes();
                _dirtyThisFrame = true;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                return false;
            }
        }
    #if UNITY_EDITOR
        public void Serialize_Editor()
        {
            var group = Undo.GetCurrentGroup();
            // if instances null or empty
            Undo.RegisterCompleteObjectUndo(this, "Modified Instances");
            var fileName =
                $"instances_{gameObject.scene.name}_{name}_{GlobalObjectId.GetGlobalObjectIdSlow(this).targetObjectId}";
            if (m_SerializedData != null && m_SerializedData.name == fileName && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m_SerializedData)))
            {
                Undo.DestroyObjectImmediate(m_SerializedData);
            }
            if (_instances != null && _instances.Count > 0)
            {
                using var ms = new MemoryStream(GetByteSize());
                if (!WriteBytes(ms)) return;
                m_SerializedData = new TextAsset(ms.ToArray()){name = fileName};
                Undo.RegisterCreatedObjectUndo(m_SerializedData, "Modified Instances");
            }
            m_SerializedVersion = CURRENT_SERIALIZED_VERSION;
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