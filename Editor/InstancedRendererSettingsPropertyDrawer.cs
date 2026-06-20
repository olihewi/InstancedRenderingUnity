using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Marinade.InstancedRendering.Editor
{
    [CustomPropertyDrawer(typeof(InstancedRendererSettings))]
    public class InstancedRendererSettingsPropertyDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var visualElement = new VisualElement();

            visualElement.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.transformSpace))));

            var renderingBox = new Box();
            renderingBox.Add(new Label("Rendering"){style = { unityFontStyleAndWeight = FontStyle.Bold }});
            visualElement.Add(renderingBox);
            var meshMaterialBox = new Box();
            renderingBox.Add(meshMaterialBox);
            meshMaterialBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.mesh))));
            meshMaterialBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.material))));
            renderingBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.lightProbeUsage))));
            renderingBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.shadowCastingMode))));
            renderingBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.reflectionProbeUsage))));
            renderingBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.renderingLayerMask))));

            var placementBox = new Box();
            placementBox.Add(new Label("Placement"){style = { unityFontStyleAndWeight = FontStyle.Bold }});
            visualElement.Add(placementBox);
            placementBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.defaultBrush))));
            placementBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.minScatterDistance))));

            return visualElement;
        }
    }
}