using UnityEditor;
using UnityEngine;

namespace Marinade.InstancedRendering.Editor
{
    internal static class CreateInstancedRenderer
    {
        [MenuItem("GameObject/Rendering/Instanced Renderer")]
        public static void CreateInstancedRendererCommand(MenuCommand menuCommand)
        {
            var go = new GameObject("Instanced Renderer");
            go.transform.SetParent(Selection.activeTransform, false);
            go.AddComponent<InstancedRenderer>();
            Selection.activeGameObject = go;
        }
    }
}