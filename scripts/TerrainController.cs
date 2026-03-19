using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;

public enum BiomeType
{
    Ocean,
    Grassland,
    Forest,
    Jungle,
    Desert,
    Savanna,
    Tundra,
    Mountain
}

[Tool]
public partial class TerrainController : Node3D
{
    [Export] private FastNoiseLite _moistureNoise = new();
    public FastNoiseLite MoistureNoise => _moistureNoise;
    [Export] private FastNoiseLite _temperatureNoise = new();
    public FastNoiseLite TemperatureNoise => _temperatureNoise;
    [Export] private FastNoiseLite _heightNoise = new();
    public FastNoiseLite HeightNoise => _heightNoise;

    [Export] private MeshInstance3D _chunkTemplate;
    [Export] private int _chunkSize = 64;
    public int ChunkSize => _chunkSize;

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

    [Export(PropertyHint.Range, "1, 24, 1, prefer_slider")]
    private int _renderDistance = 4;
    public int RenderDistance => _renderDistance;

    public Vector2I CurrentChunkCoord => _currentChunkCoord;

    [ExportGroup("Player")]
    [Export(PropertyHint.Range, "0, 10, 0.1, prefer_slider")]
    private float _playerOffset = 0.0f;
    public float PlayerOffset => _playerOffset;

    [ExportGroup("Grass")]
    [Export] private MeshInstance3D _grassTemplate;
    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    private int _grasslandDensity = 100;
    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    private int _grassForestDensity = 80;
    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    private int _grassJungleDensity = 120;
    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    private int _grassSavannaDensity = 60;
    [Export(PropertyHint.Range, "0.1f, 2.0f, 0.1f, prefer_slider")]
    private float _minGrassHeight = 0.5f;
    [Export(PropertyHint.Range, "0.1f, 2.0f, 0.1f, prefer_slider")]
    private float _maxGrassHeight = 1.5f;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.05f, prefer_slider")]
    private float _grassHeightThreshold = 0.3f;

    [ExportGroup("Trees")]
    [Export] private MeshInstance3D _treeTemplate;
    [Export(PropertyHint.Range, "0, 50, 1, prefer_slider")]
    private int _treeForestDensity = 10;
    [Export(PropertyHint.Range, "0, 50, 1, prefer_slider")]
    private int _treeJungleDensity = 20;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.05f, prefer_slider")]
    private float _treeMinHeightThreshold = 0.35f;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.05f, prefer_slider")]
    private float _treeMaxHeightThreshold = 0.65f;
    [Export(PropertyHint.Range, "0.5f, 3.0f, 0.1f, prefer_slider")]
    private float _treeMinScale = 0.8f;
    [Export(PropertyHint.Range, "0.5f, 3.0f, 0.1f, prefer_slider")]
    private float _treeMaxScale = 1.5f;

    private readonly Dictionary<Vector2I, MeshInstance3D> _chunks = [];
    private readonly object _chunkLock = new();
    private Node3D _chunkContainer;
    private Vector2I _currentChunkCoord;
    private GrassPlacer _grassPlacer;
    private TreePlacer _treePlacer;

    public override void _Ready()
    {
        _chunkContainer = new Node3D { Name = "ChunkContainer" };
        AddChild(_chunkContainer);

        _grassPlacer = new GrassPlacer(
            this,
            this,
            _grasslandDensity,
            _grassForestDensity,
            _grassJungleDensity,
            _grassSavannaDensity,
            _minGrassHeight,
            _maxGrassHeight,
            _grassHeightThreshold,
            _renderDistance);
        _grassPlacer.SetTemplate(_grassTemplate);

        _treePlacer = new TreePlacer(
            this,
            this,
            _treeForestDensity,
            _treeJungleDensity,
            _treeMinHeightThreshold,
            _treeMaxHeightThreshold,
            _treeMinScale,
            _treeMaxScale,
            _renderDistance);
        _treePlacer.SetTemplate(_treeTemplate);

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

    public float GetTerrainHeight(float worldX, float worldZ)
    {
        return _heightNoise.GetNoise2D(worldX, worldZ) * _height;
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

                bool shouldCreate;
                lock (_chunkLock)
                {
                    shouldCreate = !_chunks.ContainsKey(coord);
                }

                if (shouldCreate)
                {
                    QueueChunkGeneration(coord);
                }
            }
        }

        List<Vector2I> chunksToRemove;
        lock (_chunkLock)
        {
            chunksToRemove = [.. _chunks.Keys.Except(neededChunks)];
        }

        foreach (var coord in chunksToRemove)
        {
            RemoveChunk(coord);
        }
    }

    private void QueueChunkGeneration(Vector2I coord)
    {
        var chunkMesh = IsInstanceValid(_chunkTemplate)
            ? _chunkTemplate.Duplicate() as MeshInstance3D
            : new MeshInstance3D();

        chunkMesh.Name = $"Chunk_{coord.X}_{coord.Y}";
        chunkMesh.Position = new Vector3(coord.X * _chunkSize, 0, coord.Y * _chunkSize);
        chunkMesh.Visible = true;

        _chunkContainer.AddChild(chunkMesh);
        _chunks[coord] = chunkMesh;

        var capturedCoord = coord;
        var thread = new Thread(() =>
        {
            var mesh = GenerateChunkMeshData(capturedCoord);
            CallDeferred(nameof(AssignChunkMesh), capturedCoord, mesh);
        });
        thread.Start();
    }

    private void AssignChunkMesh(Vector2I coord, ArrayMesh mesh)
    {
        if (_chunks.TryGetValue(coord, out var chunk))
        {
            chunk.Mesh = mesh;
        }

        _grassPlacer?.GenerateForChunk(coord);
        _treePlacer?.GenerateForChunk(coord);
    }

    private ArrayMesh GenerateChunkMeshData(Vector2I coord)
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
        var colorArray = new Color[vertexArray.Length];

        var offsetX = coord.X * _chunkSize;
        var offsetZ = coord.Y * _chunkSize;

        for (int i = 0; i < vertexArray.Length; i++)
        {
            var vertex = vertexArray[i];
            var x = vertex.X + offsetX;
            var z = vertex.Z + offsetZ;

            var heightValue = GetHeight(x, z);
            vertex.Y = heightValue;

            var normal = GetNormal(x, z);
            var tangent = normal.Cross(Vector3.Up);

            var normalizedHeight = (heightValue / _height + 1.0f) / 2.0f;
            var moisture = GetMoisture(x, z);
            var temperature = GetTemperature(x, z);
            var biome = GetBiome(moisture, temperature, normalizedHeight);

            colorArray[i] = GetBiomeColorWeights(biome);

            vertexArray[i] = vertex;
            normalArray[i] = normal;
            tangentArray[4 * i] = tangent.X;
            tangentArray[4 * i + 1] = tangent.Y;
            tangentArray[4 * i + 2] = tangent.Z;
        }

        planeArrays[(int)Mesh.ArrayType.Vertex] = vertexArray.AsSpan();
        planeArrays[(int)Mesh.ArrayType.Normal] = normalArray.AsSpan();
        planeArrays[(int)Mesh.ArrayType.Tangent] = tangentArray.AsSpan();
        planeArrays[(int)Mesh.ArrayType.Color] = colorArray.AsSpan();

        var arrayMesh = new ArrayMesh();
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, planeArrays);

        return arrayMesh;
    }

    private void RemoveChunk(Vector2I coord)
    {
        if (_chunks.TryGetValue(coord, out var chunk))
        {
            chunk.QueueFree();
            _chunks.Remove(coord);
        }

        _grassPlacer?.RemoveForChunk(coord);
        _treePlacer?.RemoveForChunk(coord);
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
        var colorArray = new Color[vertexArray.Length];

        var offsetX = coord.X * _chunkSize;
        var offsetZ = coord.Y * _chunkSize;

        for (int i = 0; i < vertexArray.Length; i++)
        {
            var vertex = vertexArray[i];
            var x = vertex.X + offsetX;
            var z = vertex.Z + offsetZ;
            
            var heightValue = GetHeight(x, z);
            vertex.Y = heightValue;
            
            var normal = GetNormal(x, z);
            var tangent = normal.Cross(Vector3.Up);

            var normalizedHeight = (heightValue / _height + 1.0f) / 2.0f;
            var moisture = GetMoisture(x, z);
            var temperature = GetTemperature(x, z);
            var biome = GetBiome(moisture, temperature, normalizedHeight);
            
            colorArray[i] = GetBiomeColorWeights(biome);

            vertexArray[i] = vertex;
            normalArray[i] = normal;
            tangentArray[4 * i] = tangent.X;
            tangentArray[4 * i + 1] = tangent.Y;
            tangentArray[4 * i + 2] = tangent.Z;
        }

        planeArrays[(int)Mesh.ArrayType.Vertex] = vertexArray.AsSpan();
        planeArrays[(int)Mesh.ArrayType.Normal] = normalArray.AsSpan();
        planeArrays[(int)Mesh.ArrayType.Tangent] = tangentArray.AsSpan();
        planeArrays[(int)Mesh.ArrayType.Color] = colorArray.AsSpan();

        var arrayMesh = new ArrayMesh();
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, planeArrays);
        chunkMesh.Mesh = arrayMesh;
    }

    private void RegenerateAllChunks()
    {
        List<(Vector2I coord, MeshInstance3D mesh)> chunksToUpdate;
        lock (_chunkLock)
        {
            chunksToUpdate = _chunks.Select(kv => (kv.Key, kv.Value)).ToList();
        }

        var threads = new List<Thread>();
        var pendingMeshes = new ConcurrentDictionary<Vector2I, ArrayMesh>();

        foreach (var (coord, chunk) in chunksToUpdate)
        {
            var capturedCoord = coord;
            var thread = new Thread(() =>
            {
                var mesh = GenerateChunkMeshData(capturedCoord);
                pendingMeshes[capturedCoord] = mesh;
            });
            threads.Add(thread);
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        lock (_chunkLock)
        {
            foreach (var kvp in pendingMeshes)
            {
                if (_chunks.TryGetValue(kvp.Key, out var chunk))
                {
                    chunk.Mesh = kvp.Value;
                }
            }
        }

        if (_grassPlacer != null)
        {
            _grassPlacer.ClearAll();
            foreach (var coord in _chunks.Keys)
            {
                _grassPlacer.GenerateForChunk(coord);
            }
        }

        if (_treePlacer != null)
        {
            _treePlacer.ClearAll();
            foreach (var coord in _chunks.Keys)
            {
                _treePlacer.GenerateForChunk(coord);
            }
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

    private float GetMoisture(float x, float z)
    {
        return (_moistureNoise.GetNoise2D(x, z) + 1.0f) / 2.0f;
    }

    private float GetTemperature(float x, float z)
    {
        return (_temperatureNoise.GetNoise2D(x, z) + 1.0f) / 2.0f;
    }

    private float GetHeight(float x, float z)
    {
        return _heightNoise.GetNoise2D(x, z) * _height;
    }

    public BiomeType GetBiome(float moisture, float temperature, float height)
    {
        if (height < 0.3f)
            return BiomeType.Ocean;

        if (height > 0.7f)
            return BiomeType.Mountain;

        if (temperature < 0.2f)
            return BiomeType.Tundra;

        if (temperature > 0.7f)
            return moisture < 0.3f ? BiomeType.Desert : BiomeType.Savanna;

        return moisture switch
        {
            < 0.3f => BiomeType.Grassland,
            < 0.6f => BiomeType.Forest,
            _ => BiomeType.Jungle
        };
    }

    private Color GetBiomeColorWeights(BiomeType biome)
    {
        return biome switch
        {
            BiomeType.Ocean => new Color(0.0f, 0.2f, 0.8f),
            BiomeType.Desert => new Color(0.9f, 0.8f, 0.5f),
            BiomeType.Savanna => new Color(0.8f, 0.7f, 0.4f),
            BiomeType.Grassland => new Color(0.4f, 0.8f, 0.2f),
            BiomeType.Forest => new Color(0.2f, 0.6f, 0.1f),
            BiomeType.Jungle => new Color(0.1f, 0.5f, 0.1f),
            BiomeType.Tundra => new Color(0.8f, 0.9f, 1.0f),
            BiomeType.Mountain => new Color(0.7f, 0.7f, 0.8f),
            _ => new Color(0.5f, 0.5f, 0.5f)
        };
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
