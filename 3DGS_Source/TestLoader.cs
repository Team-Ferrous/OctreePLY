using UnityEngine;
using Kiri.Importer;

public class TestLoader : MonoBehaviour
{
    public Material splatMaterial;
    public string plyPath;

    void Start()
    {
        var go = PlyLoader.LoadPlyAsPointCloud(plyPath, splatMaterial, "3DGS_PointCloud");
        go.transform.SetParent(this.transform, worldPositionStays:false);
    }
}
