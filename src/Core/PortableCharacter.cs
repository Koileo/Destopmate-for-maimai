using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace DesktopMateOnMaimai.Core
{
    // Geometry-only interchange: no Unity 6 scripts, shaders, or serialized objects.
    internal sealed class PortableCharacter : IDisposable
    {
        public GameObject Root;
        public Transform[] Nodes;
        public SkinnedMeshRenderer[] Renderers;
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();

        public static PortableCharacter Load(string path)
        {
            var model = new PortableCharacter();
            try
            {
                using (var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8))
                    model.Read(reader, Path.GetDirectoryName(path));
                return model;
            }
            catch { model.Dispose(); throw; }
        }

        private static int Count(BinaryReader reader, int max)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > max) throw new InvalidDataException("Invalid character count " + count +
                " at byte " + (reader.BaseStream.Position - 4) + " (maximum " + max + ")");
            return count;
        }

        private static string Text(BinaryReader reader)
        {
            int length = Count(reader, 4096);
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }

        private static Vector3 Vector(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private void Read(BinaryReader r, string directory)
        {
            if (Encoding.ASCII.GetString(r.ReadBytes(8)) != "DMCHAR01")
                throw new InvalidDataException("Unsupported portable character format");
            Root = new GameObject("LocalMiku");
            int count = Count(r, 4096);
            Nodes = new Transform[count];
            int[] parents = new int[count];
            for (int i = 0; i < count; i++)
            {
                Nodes[i] = new GameObject(Text(r)).transform;
                Nodes[i].SetParent(Root.transform, false);
                parents[i] = r.ReadInt32();
                Nodes[i].localPosition = Vector(r);
                Nodes[i].localRotation = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                Nodes[i].localScale = Vector(r);
            }
            for (int i = 0; i < count; i++)
                if (parents[i] >= 0) Nodes[i].SetParent(Nodes[parents[i]], false);

            int materialCount = Count(r, 256);
            Material[] materials = new Material[materialCount];
            for (int i = 0; i < materialCount; i++)
            {
                string name = Text(r), textureFile = Text(r);
                var color = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                int transparent = r.ReadInt32();
                // Shader assets compiled for Unity 6 are deliberately replaced.
                Shader shader = Shader.Find("RD3FX/FX/Unlit/FX_Unlit_Opaque_1Tex");
                if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
                if (shader == null) throw new InvalidOperationException("Built-in character shader missing");
                var material = new Material(shader) { name = name };
                if (material.HasProperty("_Color")) material.color = color;
                if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", color);
                if (material.HasProperty("_Opacity")) material.SetFloat("_Opacity", 1f);
                if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
                if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
                if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", 1f);
                if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", 0f);
                if (transparent != 0)
                {
                    material.EnableKeyword("_ALPHATEST_ON");
                    if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0.35f);
                }
                material.renderQueue = 2000;
                MelonLogger.Msg("[DesktopMate] 材质: " + name + " / " + shader.name + " / " + color);
                _owned.Add(material);
                if (textureFile.Length > 0)
                {
                    if (Path.GetFileName(textureFile) != textureFile) throw new InvalidDataException("Invalid texture name");
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    _owned.Add(texture);
                    if (!texture.LoadImage(File.ReadAllBytes(Path.Combine(directory, textureFile))))
                        throw new InvalidDataException("Character texture could not be loaded");
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.filterMode = FilterMode.Trilinear;
                    texture.anisoLevel = 4;
                    material.mainTexture = texture;
                }
                materials[i] = material;
            }

            Renderers = new SkinnedMeshRenderer[Count(r, 128)];
            for (int m = 0; m < Renderers.Length; m++)
            {
                string name = Text(r);
                Transform node = Nodes[r.ReadInt32()];
                int boneCount = Count(r, 4096);
                var bones = new Transform[boneCount];
                var poses = new Matrix4x4[boneCount];
                for (int i = 0; i < boneCount; i++)
                {
                    bones[i] = Nodes[r.ReadInt32()];
                    for (int row = 0; row < 4; row++)
                        for (int column = 0; column < 4; column++) poses[i][row, column] = r.ReadSingle();
                }
                int verticesCount = Count(r, 200000);
                var vertices = new Vector3[verticesCount];
                var normals = new Vector3[verticesCount];
                var uv = new Vector2[verticesCount];
                var weights = new BoneWeight[verticesCount];
                for (int i = 0; i < verticesCount; i++)
                {
                    vertices[i] = Vector(r);
                    normals[i] = Vector(r);
                    uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                    weights[i] = new BoneWeight
                    {
                        boneIndex0 = r.ReadInt32(), boneIndex1 = r.ReadInt32(),
                        boneIndex2 = r.ReadInt32(), boneIndex3 = r.ReadInt32(),
                        weight0 = r.ReadSingle(), weight1 = r.ReadSingle(),
                        weight2 = r.ReadSingle(), weight3 = r.ReadSingle()
                    };
                }
                var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                _owned.Add(mesh);
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.uv = uv;
                var vertexColors = new Color32[verticesCount];
                for (int i = 0; i < vertexColors.Length; i++) vertexColors[i] = new Color32(255, 255, 255, 255);
                mesh.colors32 = vertexColors;
                mesh.boneWeights = weights;
                mesh.bindposes = poses;
                int subCount = Count(r, 256);
                mesh.subMeshCount = subCount;
                var meshMaterials = new Material[subCount];
                for (int s = 0; s < subCount; s++)
                {
                    meshMaterials[s] = materials[r.ReadInt32()];
                    int indexCount = Count(r, 2000000);
                    var indices = new int[indexCount];
                    for (int i = 0; i < indexCount; i++) indices[i] = r.ReadInt32();
                    mesh.SetTriangles(indices, s);
                }
                int shapeCount = Count(r, 256);
                for (int s = 0; s < shapeCount; s++)
                {
                    string shapeName = Text(r);
                    int sparseCount = Count(r, verticesCount);
                    var delta = new Vector3[verticesCount];
                    var dn = new Vector3[verticesCount];
                    var dt = new Vector3[verticesCount];
                    for (int i = 0; i < sparseCount; i++)
                    {
                        int index = r.ReadInt32();
                        delta[index] = Vector(r); dn[index] = Vector(r); dt[index] = Vector(r);
                    }
                    mesh.AddBlendShapeFrame(shapeName, 100f, delta, dn, dt);
                }
                mesh.RecalculateBounds();
                var renderer = node.gameObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.sharedMaterials = meshMaterials;
                renderer.bones = bones;
                renderer.rootBone = bones[0];
                renderer.updateWhenOffscreen = true;
                renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 5f);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                Renderers[m] = renderer;
            }
            if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("Unexpected trailing character data");
        }

        public void Dispose()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
            foreach (var obj in _owned) if (obj != null) UnityEngine.Object.Destroy(obj);
            _owned.Clear();
        }
    }
}
