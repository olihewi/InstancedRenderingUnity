using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Marinade.InstancedRendering.Editor
{
    public class InstanceScatteringOverlay : Overlay
    {
        // Tabs
        public TabView tabView;
        public Tab scatteringTab, singleTab;
        // Scattering
        public Slider radiusSlider, falloffSlider, scatterMultiplier;
        public Toggle requireSameCollider;
        public LayerMaskField layerMask;

        public InstanceScatteringBrush sourceBrush;
        public InstanceScatteringBrush brush;
        
        public override VisualElement CreatePanelContent()
        {
            sourceBrush = null;
            var rootElement = new VisualElement()
            {
                name = "Instance Scattering",
            };
            tabView = new TabView();
            rootElement.Add(tabView);
            
            tabView.Add(scatteringTab = new Tab("Scattering"));
            scatteringTab.Add(radiusSlider = new Slider("", 0F, 25F));
            radiusSlider.RegisterCallback<ChangeEvent<float>>(f =>
            {
                if (brush == null) return;
                brush.outerRadius = f.newValue;
                brush.innerRadius = f.newValue * (falloffSlider.value);
            });
            scatteringTab.Add(falloffSlider = new Slider("Hardness", 0F, 1F));
            falloffSlider.RegisterCallback<ChangeEvent<float>>(f =>
            {
                if (brush == null) return;
                brush.innerRadius = radiusSlider.value * (f.newValue);
            });
            scatteringTab.Add(scatterMultiplier = new Slider("Scatter Multiplier", 1F, 10F){value = 1F});
            scatterMultiplier.RegisterCallback<ChangeEvent<float>>(f =>
            {
                if (brush == null || sourceBrush == null) return;
                brush.scatterDistance = sourceBrush.scatterDistance * f.newValue;
            });
            scatteringTab.Add(requireSameCollider = new Toggle("Scatter on Same Collider"));
            requireSameCollider.RegisterCallback<ChangeEvent<bool>>(b =>
            {
                if (brush == null) return;
                brush.requireSameCollider = b.newValue;
            });
            scatteringTab.Add(layerMask = new LayerMaskField("Layer Mask", Physics.DefaultRaycastLayers));
            layerMask.RegisterCallback<ChangeEvent<int>>(i =>
            {
                if (brush == null) return;
                brush.layerMask = i.newValue;
            });

            tabView.Add(singleTab = new Tab("Single"));
            return rootElement;
        }

        public void SetBrush(InstanceScatteringBrush source)
        {
            if (tabView == null || (sourceBrush == source && brush != null)) return;
            if (brush != null) Object.DestroyImmediate(brush);
            sourceBrush = source;
            if (source != null)
                brush = Object.Instantiate(source);
            else
                brush = ScriptableObject.CreateInstance<InstanceScatteringBrush>();
            
            falloffSlider.value = brush.innerRadius / brush.outerRadius;
            radiusSlider.value = brush.outerRadius;
            requireSameCollider.value = brush.requireSameCollider;
            layerMask.value = brush.layerMask;
        }
    }
}