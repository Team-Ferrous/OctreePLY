// Kiri3DGSImporter.cs
// Minimal, self-contained PLY importer for Unity (points/splats).
// Compile into a .NET assembly for Unity or drop into Assets/Scripts for quick testing.

using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Kiri.Importer
{
    // Simple MonoBehaviour to hold point-cloud data (colors, sizes) for shader consumption.
    public class PointCloudRenderer : MonoBehaviour
    {
        public Mesh mesh;
        public Material material;

        // Optional arrays to store sizes/colors so a custom shader or compute step can read them.
        public Vector4[] extraVertexData; // e.g. (size,unused,unused,unused) or pack other attributes
        public Color[] colors;

        void Reset()
        {
            if (mesh == null) mesh = new Mesh();
        }

        public void Setup(Mesh m, Color[] cols, Vector4[] extra)
        {
            mesh = m;
            colors = cols;
            extraVertexData = extra;

            var mf = gameObject.GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            var mr = gameObject.GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();

            mf.sharedMesh = mesh;
            mr.sharedMaterial = material;
            if (cols != null && cols.Length == mesh.vertexCount)
            {
                mesh.colors = cols;
            }

            // If you want to send 'size' per-vertex to shader, packing into uv2 is one simple route:
            if (extra != null && extra.Length == mesh.vertexCount)
            {
                var uv2 = new Vector2[mesh.vertexCount];
                for (int i = 0; i < mesh.vertexCount; i++) uv2[i] = new Vector2(extra[i].x, extra[i].y);
                mesh.uv2 = uv2;
            }
        }
    }

    public static class PlyLoader
    {
        // Public API: loads PLY file and returns GameObject with Mesh+PointCloudRenderer
        public static GameObject LoadPlyAsPointCloud(string path, Material defaultMaterial = null, string goName = null)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);

            PlyData ply = ParsePly(path);

            // Create Unity Mesh
            Mesh mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            Vector3[] vertices = ply.vertices.ToArray();
            mesh.vertices = vertices;

            // Use point topology
            int[] indices = new int[vertices.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;

            mesh.SetIndices(indices, MeshTopology.Points, 0, calculateBounds: true);

            // Colors
            Color[] cols = null;
            if (ply.colors != null && ply.colors.Count == vertices.Length)
            {
                cols = ply.colors.ToArray();
                mesh.colors = cols;
            }

            // Normals
            if (ply.normals != null && ply.normals.Count == vertices.Length)
            {
                mesh.normals = ply.normals.ToArray();
            }

            // Extra per-vertex (size, etc) -> pack into Vector4 array
            Vector4[] extra = null;
            if (ply.sizes != null && ply.sizes.Count == vertices.Length)
            {
                extra = new Vector4[vertices.Length];
                for (int i = 0; i < vertices.Length; i++) extra[i] = new Vector4(ply.sizes[i], 0, 0, 0);
            }

            var go = new GameObject(goName ?? Path.GetFileNameWithoutExtension(path));
            var pcl = go.AddComponent<PointCloudRenderer>();
            pcl.material = defaultMaterial;
            pcl.Setup(mesh, cols, extra);

            return go;
        }

        // Internal data container
        public class PlyData
        {
            public List<Vector3> vertices = new List<Vector3>();
            public List<Vector3> normals = new List<Vector3>();
            public List<Color> colors = new List<Color>();
            public List<float> sizes = new List<float>();
        }

        // Very lightweight PLY parser handling ASCII and binary little-endian.
        public static PlyData ParsePly(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                // Read header (ASCII)
                var headerSb = new StringBuilder();
                string line;
                // Read line-by-line using ASCII - we must move the stream pointer manually
                fs.Seek(0, SeekOrigin.Begin);
                var sr = new StreamReader(fs, Encoding.ASCII);
                bool isBinary = false;
                bool isLittleEndian = true;
                long headerEndPos = 0;
                List<ElementDef> elements = new List<ElementDef>();
                ElementDef currentElement = null;

                while ((line = sr.ReadLine()) != null)
                {
                    headerSb.AppendLine(line);
                    headerEndPos = fs.Position;
                    line = line.Trim();
                    if (line.StartsWith("format"))
                    {
                        if (line.Contains("binary_little_endian")) { isBinary = true; isLittleEndian = true; }
                        else if (line.Contains("binary_big_endian")) { isBinary = true; isLittleEndian = false; }
                        else isBinary = false; // ascii
                    }
                    else if (line.StartsWith("element"))
                    {
                        // element <name> <count>
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            currentElement = new ElementDef { name = parts[1], count = int.Parse(parts[2]) };
                            elements.Add(currentElement);
                        }
                    }
                    else if (line.StartsWith("property"))
                    {
                        // property <type> <name>   OR property list ...
                        if (currentElement == null) continue;
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3 && parts[1] != "list")
                        {
                            currentElement.properties.Add(new PropertyDef { type = parts[1], name = parts[2] });
                        }
                    }
                    else if (line == "end_header")
                    {
                        headerEndPos = fs.Position;
                        break;
                    }
                }

                // Seek to data start
                fs.Seek(headerEndPos, SeekOrigin.Begin);

                // Create parser based on elements (we only care about 'vertex' typically)
                PlyData outData = new PlyData();

                foreach (var elem in elements)
                {
                    if (elem.name == "vertex")
                    {
                        if (isBinary)
                        {
                            // binary reading
                            for (int i = 0; i < elem.count; i++)
                            {
                                float x = 0, y = 0, z = 0;
                                float nx = 0, ny = 0, nz = 0;
                                float r = -1, g = -1, b = -1, a = -1;
                                float size = -1;
                                foreach (var p in elem.properties)
                                {
                                    object val = ReadBinaryProperty(br, p.type, isLittleEndian);
                                    // Map common names
                                    switch (p.name.ToLower())
                                    {
                                        case "x": x = Convert.ToSingle(val); break;
                                        case "y": y = Convert.ToSingle(val); break;
                                        case "z": z = Convert.ToSingle(val); break;
                                        case "nx": nx = Convert.ToSingle(val); break;
                                        case "ny": ny = Convert.ToSingle(val); break;
                                        case "nz": nz = Convert.ToSingle(val); break;
                                        case "red": case "r": r = Convert.ToSingle(val) / 255f; break;
                                        case "green": case "g": g = Convert.ToSingle(val) / 255f; break;
                                        case "blue": case "b": b = Convert.ToSingle(val) / 255f; break;
                                        case "alpha": a = Convert.ToSingle(val) / 255f; break;
                                        case "size": case "radius": size = Convert.ToSingle(val); break;
                                        default:
                                            // could store custom attributes if needed
                                            break;
                                    }
                                }
                                outData.vertices.Add(new Vector3(x, y, z));
                                if (!(nx == 0 && ny == 0 && nz == 0)) outData.normals.Add(new Vector3(nx, ny, nz));
                                if (r >= 0 && g >= 0 && b >= 0)
                                {
                                    var col = new Color(r, g, b, a >= 0 ? a : 1f);
                                    outData.colors.Add(col);
                                }
                                if (size >= 0) outData.sizes.Add(size);
                            }
                        }
                        else
                        {
                            // ASCII
                            var srText = new StreamReader(fs, Encoding.ASCII);
                            for (int i = 0; i < elem.count; i++)
                            {
                                string dataline = srText.ReadLine();
                                if (dataline == null) break;
                                var parts = dataline.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                int idx = 0;
                                float x = 0, y = 0, z = 0;
                                float nx = 0, ny = 0, nz = 0;
                                float r = -1, g = -1, b = -1, a = -1;
                                float size = -1;
                                foreach (var p in elem.properties)
                                {
                                    if (idx >= parts.Length) break;
                                    var sval = parts[idx++];
                                    switch (p.name.ToLower())
                                    {
                                        case "x": x = float.Parse(sval); break;
                                        case "y": y = float.Parse(sval); break;
                                        case "z": z = float.Parse(sval); break;
                                        case "nx": nx = float.Parse(sval); break;
                                        case "ny": ny = float.Parse(sval); break;
                                        case "nz": nz = float.Parse(sval); break;
                                        case "red": case "r": r = float.Parse(sval) / 255f; break;
                                        case "green": case "g": g = float.Parse(sval) / 255f; break;
                                        case "blue": case "b": b = float.Parse(sval) / 255f; break;
                                        case "alpha": a = float.Parse(sval) / 255f; break;
                                        case "size": case "radius": size = float.Parse(sval); break;
                                        default:
                                            break;
                                    }
                                }
                                outData.vertices.Add(new Vector3(x, y, z));
                                if (!(nx == 0 && ny == 0 && nz == 0)) outData.normals.Add(new Vector3(nx, ny, nz));
                                if (r >= 0 && g >= 0 && b >= 0) outData.colors.Add(new Color(r, g, b, a >= 0 ? a : 1f));
                                if (size >= 0) outData.sizes.Add(size);
                            }
                            break; // ascii read uses StreamReader and consumes rest
                        }
                    }
                    else
                    {
                        // Skip other elements (faces, etc) for now.
                        if (isBinary)
                        {
                            // We don't parse these now: advance stream by appropriate bytes if needed.
                            // For simplicity we skip them; future work: parse faces if needed.
                        }
                        else
                        {
                            // If ascii and not vertex, we need to skip lines equal to elem.count.
                            var srText = new StreamReader(fs, Encoding.ASCII);
                            for (int i = 0; i < elem.count; i++) srText.ReadLine();
                        }
                    }
                }

                return outData;
            }
        }

        // Helper: read one binary property value according to PLY type string
        private static object ReadBinaryProperty(BinaryReader br, string type, bool littleEndian)
        {
            // List of common PLY types: char, uchar, short, ushort, int, uint, float, double
            // Map to C# reads; BinaryReader is little-endian by default on most platforms; handle big-endian if necessary.
            switch (type)
            {
                case "char":
                case "int8": return (sbyte)br.ReadSByte();
                case "uchar":
                case "uint8": return (byte)br.ReadByte();
                case "short":
                case "int16": return (short)br.ReadInt16();
                case "ushort":
                case "uint16": return (ushort)br.ReadUInt16();
                case "int":
                case "int32": return (int)br.ReadInt32();
                case "uint":
                case "uint32": return (uint)br.ReadUInt32();
                case "float":
                case "float32": return (float)br.ReadSingle();
                case "double":
                case "float64": return (double)br.ReadDouble();
                default:
                    // fallback: try float
                    return (float)br.ReadSingle();
            }
        }

        // Small helper types for header parsing
        private class ElementDef
        {
            public string name;
            public int count;
            public List<PropertyDef> properties = new List<PropertyDef>();
        }
        private class PropertyDef
        {
            public string type;
            public string name;
        }
    }
}
