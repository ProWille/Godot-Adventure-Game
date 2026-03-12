using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

[Tool]
public partial class TerrainController : Node3D
{
    [Export] private FastNoiseLite _noise = new();
    public FastNoiseLite Noise => _noise;

    [Export(PropertyHint.Range, "4, 256, 4, prefer_slider")]
    private int _resolution = 32;
    public int Resolution
    {
        get => _resolution;
        set
        {
            _resolution = Math.Clamp(value, 4, 256);
            RegenerateAllChunks();
        }
    }

    [Export(PropertyHint.Range, "4.0f, 256.0f, 4.0f, prefer_slider")]
    private float _height = 64.0f;
    public float Height
    {
        get => _height;
        set
        {
            _height = Math.Clamp(value, 4.0f, 256.0f);
            RegenerateAllChunks();
        }
    }

    [Export(PropertyHint.Range, "1, 10, 1, prefer_slider")]
    private int _renderDistance = 4;
    public int RenderDistance => _renderDistance;

    [Export] private int _chunkSize = 64;
    public int ChunkSize => _chunkSize;

    [Export] private MeshInstance3D _chunkTemplate;
    public MeshInstance3D ChunkTemplate => _chunkTemplate;

    private readonly Dictionary<Vector2I, MeshInstance3D> _chunks = [];
    private Node3D _chunkContainer;
    private Vector2I _currentChunkCoord;

    public override void _Ready()
    {
        _chunkContainer = new Node3D { Name = "ChunkContainer" };
        AddChild(_chunkContainer);

        if (!Engine.IsEditorHint())
        {
            UpdateChunksForPosition(Vector3.Zero);
        }
    }

    public override void _Process(double delta)
    {
        var player = GetTree().CurrentScene?.GetNode<Node3D>("Player");
        if (IsInstanceValid(player))
        {
            UpdateChunksForPosition(player.GlobalPosition);
        }
    }

    private void UpdateChunksForPosition(Vector3 position)
    {
        var newChunkCoord = GetChunkCoord(position.X, position.Z);

        if (newChunkCoord == _currentChunkCoord)
            return;

        _currentChunkCoord = newChunkCoord;
        LoadChunksAroundPlayer();
    }

    private void LoadChunksAroundPlayer()
    {
        var neededChunks = new HashSet<Vector2I>();

        for (int x = -_renderDistance; x <= _renderDistance; x++)
        {
            for (int z = -_renderDistance; z <= _renderDistance; z++)
            {
                var coord = new Vector2I(_currentChunkCoord.X + x, _currentChunkCoord.Y + z);
                neededChunks.Add(coord);

                if (!_chunks.ContainsKey(coord))
                {
                    CreateChunk(coord);
                }
            }
        }

        foreach (var coord in _chunks.Keys.Except(neededChunks))
        {
            RemoveChunk(coord);
        }
    }

    private void CreateChunk(Vector2I coord)
    {
        var chunkMesh = IsInstanceValid(_chunkTemplate)
            ? _chunkTemplate.Duplicate() as MeshInstance3D
            : new MeshInstance3D();

        chunkMesh.Name = $"Chunk_{coord.X}_{coord.Y}";
        chunkMesh.Position = new Vector3(coord.X * _chunkSize, 0, coord.Y * _chunkSize);
        chunkMesh.Visible = true;

        GenerateChunkMesh(chunkMesh, coord);
        _chunkContainer.AddChild(chunkMesh);
        _chunks[coord] = chunkMesh;
    }

    private void RemoveChunk(Vector2I coord)
    {
        if (_chunks.TryGetValue(coord, out var chunk))
        {
            chunk.QueueFree();
            _chunks.Remove(coord);
        }
    }

    private void GenerateChunkMesh(MeshInstance3D chunkMesh, Vector2I coord)
    {
        var plane = new PlaneMesh
        {
            SubdivideDepth = _resolution,
            SubdivideWidth = _resolution,
            Size = new Vector2(_chunkSize, _chunkSize)
        };

        var planeArrays = plane.GetMeshArrays();

        var vertexArray = planeArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normalArray = planeArrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var tangentArray = planeArrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array();

        var offsetX = coord.X * _chunkSize;
        var offsetZ = coord.Y * _chunkSize;

        for (int i = 0; i < vertexArray.Length; i++)
        {
            var vertex = vertexArray[i];
            vertex.Y = GetHeight(vertex.X + offsetX, vertex.Z + offsetZ);
            var normal = GetNormal(vertex.X + offsetX, vertex.Z + offsetZ);
            var tangent = normal.Cross(Vector3.Up);

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
        chunkMesh.Mesh = arrayMesh;
    }

    private void RegenerateAllChunks()
    {
        foreach (var chunk in _chunks.Values)
        {
            var coord = GetChunkCoord(chunk.Position.X, chunk.Position.Z);
            GenerateChunkMesh(chunk, coord);
        }
    }

    private Vector2I GetChunkCoord(float x, float z)
    {
        return new Vector2I((int)Math.Floor(x / _chunkSize), (int)Math.Floor(z / _chunkSize));
    }

    private Vector2I GetChunkCoord(Vector3 position)
    {
        return GetChunkCoord(position.X, position.Z);
    }

    private float GetHeight(float x, float z)
    {
        return _noise.GetNoise2D(x, z) * _height;
    }

    private Vector3 GetNormal(float x, float z)
    {
        var epsilon = (float)_chunkSize / _resolution;
        var normal = new Vector3(
            (GetHeight(x + epsilon, z) - GetHeight(x - epsilon, z)) / (2.0f * epsilon),
            1.0f,
            (GetHeight(x, z + epsilon) - GetHeight(x, z - epsilon)) / (2.0f * epsilon)
        );

        return normal.Normalized();
    }
}
