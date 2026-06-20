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
            var meshMaterialBox = new Box();
            renderingBox.Add(meshMaterialBox);
            meshMaterialBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.mesh))));
            meshMaterialBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.material))));
            renderingBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.lightProbeUsage))));
            renderingBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.shadowCastingMode))));
            renderingBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.reflectionProbeUsage))));
            renderingBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.renderingLayerMask))));
            visualElement.Add(renderingBox);

            var placementBox = new Box();
            placementBox.Add(new Label("Placement"){style = { unityFontStyleAndWeight = FontStyle.Bold }});
            placementBox.Add(new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.minScatterDistance))));
            var brushProperty = new PropertyField(property.FindPropertyRelative(nameof(InstancedRendererSettings.brush)));
            placementBox.Add(brushProperty);
            var brushFoldout = new Foldout(){text = "Edit Brush"};
            brushProperty.RegisterValueChangeCallback(e =>
            {
                brushFoldout.Clear();
                brushFoldout.visible = false;
                var brushBox = new Box();
                if (e.changedProperty.hasMultipleDifferentValues ||
                    e.changedProperty.objectReferenceValue == null) return;
                var so = new SerializedObject(e.changedProperty.objectReferenceValue);
                var it = so.GetIterator();
                it.NextVisible(true);
                while (it.NextVisible(false))
                {
                    var field = new PropertyField(it);
                    field.Bind(so);
                    brushBox.Add(field);
                }
                brushFoldout.Add(brushBox);
                brushFoldout.visible = true;
            });
            placementBox.Add(brushFoldout);
            visualElement.Add(placementBox);

            return visualElement;
        }
    }
}