using System;
using Godot;

[Tool]
public partial class TerrainController : MeshInstance3D
{
    [Export] public int MapSize { get; private set; } = 256;
    [Export] public FastNoiseLite Noise { get; private set; } = new();

    [Export(PropertyHint.Range, "4, 256, 4, prefer_slider")]
    private int _resolution = 32;
    public int Resolution
    {
        get => _resolution;
        private set
        {
            _resolution = Math.Clamp(value, 4, 256);
        }
    }

    [Export(PropertyHint.Range, "4.0f, 256.0f, 4.0f, prefer_slider")]
    private float _height = 64.0f;
    public float Height
    {
        get => _height;
        private set
        {
            _height = Math.Clamp(value, 4.0f, 256.0f);
        }
    }

    public override void _Ready()
    {
        SetInstanceShaderParameter("height", _height * 2);
        UpdateMesh();
    }

    private float GetHeight(float x, float y)
    {
        return Noise.GetNoise2D(x, y) * Height;
    }

    private Vector3 GetNormal(float x, float y)
    {
        var epsilon = MapSize / Resolution;
        var normal = new Vector3(
            (GetHeight(x + epsilon, y) - GetHeight(x - epsilon, y)) / (2.0f * epsilon),
            1.0f,
            (GetHeight(x, y + epsilon) - GetHeight(x, y - epsilon)) / (2.0f * epsilon)
        );

        return normal.Normalized();
    }

    private void UpdateMesh()
    {
        var plane = new PlaneMesh
        {
            SubdivideDepth = Resolution,
            SubdivideWidth = Resolution,
            Size = new Vector2(MapSize, MapSize)
        };

        var planeArrays = plane.GetMeshArrays();

        var vertexArray = planeArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normalArray = planeArrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var tangentArray = planeArrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array();

        for (int i = 0; i < vertexArray.Length; i++)
        {
            var vertex = vertexArray[i];
            var normal = Vector3.Up;
            var tangent = Vector3.Right;
            if (Noise != null)
            {
                vertex.Y = GetHeight(vertex.X, vertex.Z);
                normal = GetNormal(vertex.X, vertex.Z);
                tangent = normal.Cross(Vector3.Up);
            }

            vertexArray[i] = vertex;
            normalArray[i] = normal;
            tangentArray[4 * i] = tangent.X;
            tangentArray[4 * i + 1] = tangent.Y;
            tangentArray[4 * i + 2] = tangent.Z;
        }

        planeArrays[(int)Mesh.ArrayType.Vertex] = vertexArray.AsSpan();
        planeArrays[(int)Mesh.ArrayType.Normal] = normalArray.AsSpan();
        planeArrays[(int)Mesh.ArrayType.Tangent] = tangentArray.AsSpan();

        var arrayMesh = new ArrayMesh();
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, planeArrays);
        Mesh = arrayMesh;
    }
}
