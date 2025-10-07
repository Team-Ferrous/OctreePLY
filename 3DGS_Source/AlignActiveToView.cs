using System.Collections.Generic;
using UnityEngine;

namespace Kiri3DGS
{
    public class AlignActiveToView : MonoBehaviour
    {
        public void AlignToAxisX(GameObject target)
        {
            if (!target) return;
            target.transform.rotation = Quaternion.identity; // world X-aligned
            UpdateRenderModifier(target);
        }

        public void AlignToAxisY(GameObject target)
        {
            if (!target) return;
            target.transform.rotation = Quaternion.Euler(0, 90, 0); // world Y-aligned
            UpdateRenderModifier(target);
        }

        public void AlignToAxisZ(GameObject target)
        {
            if (!target) return;
            target.transform.rotation = Quaternion.Euler(90, 0, 0); // world Z-aligned
            UpdateRenderModifier(target);
        }

        public void AlignToView(GameObject target, Camera cam)
        {
            if (!target || !cam) return;

            // Pass camera matrices to the modifier
            MeshModifier3DGS modifier = target.GetComponent<MeshModifier3DGS>();
            if (modifier != null)
            {
                modifier.SetViewMatrix(cam.worldToCameraMatrix);
                modifier.SetProjectionMatrix(cam.projectionMatrix);
                modifier.SetWindowSize(cam.pixelWidth, cam.pixelHeight);
                modifier.ApplyUpdate();
                Debug.Log($"Updated 3DGS_Render modifier for {target.name}");
            }
        }

        private void UpdateRenderModifier(GameObject target)
        {
            MeshModifier3DGS modifier = target.GetComponent<MeshModifier3DGS>();
            if (modifier != null)
            {
                modifier.ApplyUpdate();
            }
        }
    }
}
