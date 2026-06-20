using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Marinade.InstancedRendering.Editor
{
    [EditorTool("Instance Scattering", typeof(InstancedRenderer))]
    [Icon("Packages/com.marinade.instancedrendering/Editor/Icons/icon_ScatteringBrush.png")]
    public class InstanceScatteringTool : EditorTool
    {
        
        private bool mouseDown = false;
        private InstanceScatteringOverlay overlay;
        public override void OnActivated()
        {
            base.OnActivated();
            SceneView.AddOverlayToActiveView(overlay = new InstanceScatteringOverlay());
            overlay.displayed = true;
            overlay.displayName = "Instance Scattering";
        }

        public override void OnWillBeDeactivated()
        {
            base.OnWillBeDeactivated();
            SceneView.RemoveOverlayFromActiveView(overlay);
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (target is not InstancedRenderer instancedRenderer || overlay == null) return;
            overlay.SetBrush(instancedRenderer.Brush);
            int control = GUIUtility.GetControlID(64325437, FocusType.Passive);
            var e = Event.current;
            var posSS = e.mousePosition;
            var ray = HandleUtility.GUIPointToWorldRay(posSS);
            if (!Physics.Raycast(ray, out var hitInfo, Mathf.Infinity, overlay.brush.layerMask)) return;
            Handles.color = e.shift ? Color.red : Color.white;
            var outerRadius = overlay.radiusSlider.value;
            var innerRadius = outerRadius * overlay.falloffSlider.value;
            if (overlay.tabView.activeTab == overlay.scatteringTab) Handles.DrawWireDisc(hitInfo.point, hitInfo.normal, e.shift ? outerRadius : innerRadius);
            Handles.color = new Color(Handles.color.r,Handles.color.g,Handles.color.b,0.25F);
            if (!e.shift && overlay.tabView.activeTab == overlay.scatteringTab) Handles.DrawWireDisc(hitInfo.point, hitInfo.normal, outerRadius);
            Handles.DrawLine(hitInfo.point, hitInfo.point + hitInfo.normal * outerRadius);
            //if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);

            if (e.button == 0 && e.type == EventType.MouseDown) mouseDown = true;
            if (!mouseDown) return;
            switch (e.type)
            {
            case EventType.MouseDown or EventType.MouseMove or EventType.MouseDrag:
                if (!e.shift)
                {
                    if (overlay.tabView.activeTab == overlay.scatteringTab)
                    {
                        instancedRenderer.Scatter(ray, hitInfo, overlay.brush);
                    }
                    else if (e.type == EventType.MouseDown)
                    {
                        var rot = overlay.brush.GetInstanceRotation(ray, hitInfo);
                        instancedRenderer.AddInstance(Matrix4x4.TRS(hitInfo.point + rot * overlay.brush.pivot,
                            rot,
                            overlay.brush.GetInstanceScale(hitInfo.point)));
                    }
                }
                else
                {
                    if (overlay.tabView.activeTab == overlay.scatteringTab)
                    {
                        instancedRenderer.RemoveSphere(hitInfo.point, outerRadius);
                    }
                    else
                    {
                        var closestIdx =
                            instancedRenderer.GetFirstOverlappingInstance(hitInfo.point, instancedRenderer.Settings.minScatterDistance);
                        if (closestIdx >= 0) instancedRenderer.RemoveInstance(closestIdx);
                    }
                }
                e.Use();
                break;
            case EventType.MouseUp or EventType.MouseLeaveWindow:
                mouseDown = false;
                e.Use();
                instancedRenderer.Serialize_Editor();
                break;
            }
        }
    }
}