using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;

namespace AdventureGame.Scripts;

public enum BiomeType
{
    Ocean,
    Grassland,
    Forest,
    Jungle,
    Desert,
    Savanna,
    Tundra,
    Mountain,
    Sand,
    Snow
}

public partial class TerrainController : Node3D
{
    public Vector2I CurrentChunkCoord => _currentChunkCoord;

    [ExportGroup("Chunk Settings")]
    private FastNoiseLite _moistureNoise = new();
    [Export] public FastNoiseLite MoistureNoise
    {
        get => _moistureNoise;
        set => _moistureNoise = value;
    }

    private FastNoiseLite _temperatureNoise = new();
    [Export] public FastNoiseLite TemperatureNoise
    {
        get => _temperatureNoise;
        set => _temperatureNoise = value;
    }

    private FastNoiseLite _heightNoise = new();
    [Export] public FastNoiseLite HeightNoise
    {
        get => _heightNoise;
        set => _heightNoise = value;
    }

    private MeshInstance3D _chunkTemplate;
    [Export] public MeshInstance3D ChunkTemplate
    {
        get => _chunkTemplate;
        set => _chunkTemplate = value;
    }

    private int _chunkSize = 64;
    [Export] public int ChunkSize
    {
        get => _chunkSize;
        set => _chunkSize = value;
    }

    private int _resolution = 32;
    [Export(PropertyHint.Range, "4, 256, 4, prefer_slider")]
    public int Resolution
    {
        get => _resolution;
        set { _resolution = Math.Clamp(value, 4, 256); RegenerateAllChunks(); }
    }

    private float _height = 64.0f;
    [Export(PropertyHint.Range, "4.0f, 256.0f, 4.0f, prefer_slider")]
    public float Height
    {
        get => _height;
        set { _height = Math.Clamp(value, 4.0f, 256.0f); RegenerateAllChunks(); }
    }

    private int _renderDistance = 4;
    [Export(PropertyHint.Range, "1, 24, 1, prefer_slider")]
    public int RenderDistance
    {
        get => _renderDistance;
        set { _renderDistance = value; }
    }

    [Export] public MeshInstance3D WaterTemplate;

    [ExportGroup("Biome Blend")]
    [Export(PropertyHint.Range, "0.0, 0.5, 0.01, prefer_slider")]
    private float _blendEdgeWidth = 0.1f;
    public float BlendEdgeWidth
    {
        get => _blendEdgeWidth;
        set => _blendEdgeWidth = value;
    }

    [ExportGroup("Biome Thresholds")]
    private float _oceanHeightThreshold = 0.4f;
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05, prefer_slider")]
    public float OceanHeightThreshold
    {
        get => _oceanHeightThreshold;
        set { _oceanHeightThreshold = Math.Clamp(value, 0.0f, 1.0f); RegenerateAllChunks(); }
    }

    private float _mountainHeightThreshold = 0.7f;
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05, prefer_slider")]
    public float MountainHeightThreshold
    {
        get => _mountainHeightThreshold;
        set { _mountainHeightThreshold = Math.Clamp(value, 0.0f, 1.0f); RegenerateAllChunks(); }
    }

    private float _tundraTemperatureThreshold = 0.2f;
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05, prefer_slider")]
    public float TundraTemperatureThreshold
    {
        get => _tundraTemperatureThreshold;
        set { _tundraTemperatureThreshold = Math.Clamp(value, 0.0f, 1.0f); RegenerateAllChunks(); }
    }

    private float _desertTemperatureThreshold = 0.7f;
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05, prefer_slider")]
    public float DesertTemperatureThreshold
    {
        get => _desertTemperatureThreshold;
        set { _desertTemperatureThreshold = Math.Clamp(value, 0.0f, 1.0f); RegenerateAllChunks(); }
    }

    private float _desertMoistureThreshold = 0.3f;
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05, prefer_slider")]
    public float DesertMoistureThreshold
    {
        get => _desertMoistureThreshold;
        set { _desertMoistureThreshold = Math.Clamp(value, 0.0f, 1.0f); RegenerateAllChunks(); }
    }

    private float _sandMoistureThreshold = 0.15f;
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05, prefer_slider")]
    public float SandMoistureThreshold
    {
        get => _sandMoistureThreshold;
        set { _sandMoistureThreshold = Math.Clamp(value, 0.0f, 1.0f); RegenerateAllChunks(); }
    }

    private float _grasslandMoistureThreshold = 0.3f;
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05, prefer_slider")]
    public float GrasslandMoistureThreshold
    {
        get => _grasslandMoistureThreshold;
        set { _grasslandMoistureThreshold = Math.Clamp(value, 0.0f, 1.0f); RegenerateAllChunks(); }
    }

    private float _forestMoistureThreshold = 0.6f;
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05, prefer_slider")]
    public float ForestMoistureThreshold
    {
        get => _forestMoistureThreshold;
        set { _forestMoistureThreshold = Math.Clamp(value, 0.0f, 1.0f); RegenerateAllChunks(); }
    }

    [ExportGroup("Player Settings")]
    private float _playerOffset = 0.0f;
    [Export(PropertyHint.Range, "0, 10, 0.1, prefer_slider")]
    public float PlayerOffset
    {
        get => _playerOffset;
        set => _playerOffset = value;
    }

    [ExportGroup("Grass Settings")]
    private bool _grassEnabled = true;
    [Export] public bool GrassEnabled
    {
        get => _grassEnabled;
        set
        {
            if (_grassEnabled == value)
                return;
            _grassEnabled = value;
            if (_grassEnabled)
            {
                InitializeGrassPlacer();
            }
            else
            {
                _grassPlacer?.ClearAll();
                _grassPlacer = null;
            }
        }
    }

    private MeshInstance3D _grassTemplate;
    [Export] public MeshInstance3D GrassTemplate
    {
        get => _grassTemplate;
        set => _grassTemplate = value;
    }

    private int _grasslandDensity = 100;
    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    public int GrasslandDensity
    {
        get => _grasslandDensity;
        set => _grasslandDensity = value;
    }

    private int _grassForestDensity = 80;
    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    public int GrassForestDensity
    {
        get => _grassForestDensity;
        set => _grassForestDensity = value;
    }

    private int _grassJungleDensity = 120;
    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    public int GrassJungleDensity
    {
        get => _grassJungleDensity;
        set => _grassJungleDensity = value;
    }

    private int _grassSavannaDensity = 60;
    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    public int GrassSavannaDensity
    {
        get => _grassSavannaDensity;
        set => _grassSavannaDensity = value;
    }

    private float _minGrassHeight = 0.5f;
    [Export(PropertyHint.Range, "0.1f, 2.0f, 0.1f, prefer_slider")]
    public float MinGrassHeight
    {
        get => _minGrassHeight;
        set => _minGrassHeight = value;
    }

    private float _maxGrassHeight = 1.5f;
    [Export(PropertyHint.Range, "0.1f, 2.0f, 0.1f, prefer_slider")]
    public float MaxGrassHeight
    {
        get => _maxGrassHeight;
        set => _maxGrassHeight = value;
    }

    private float _grassHeightOffset = 0.3f;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.05f, prefer_slider")]
    public float GrassHeightOffset
    {
        get => _grassHeightOffset;
        set => _grassHeightOffset = value;
    }

    private float _grassGridSpacing = 1.0f;
    [Export(PropertyHint.Range, "0.2f, 5.0f, 0.1f, prefer_slider")]
    public float GrassGridSpacing
    {
        get => _grassGridSpacing;
        set => _grassGridSpacing = value;
    }

    private float _grassDensityThreshold = 0.3f;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.05f, prefer_slider")]
    public float GrassDensityThreshold
    {
        get => _grassDensityThreshold;
        set => _grassDensityThreshold = value;
    }

    private float _grassPlacementFrequency = 0.8f;
    [Export(PropertyHint.Range, "0.01f, 2.0f, 0.01f, prefer_slider")]
    public float GrassPlacementFrequency
    {
        get => _grassPlacementFrequency;
        set => _grassPlacementFrequency = value;
    }

    private float _grassVariationFrequency = 0.1f;
    [Export(PropertyHint.Range, "0.01f, 2.0f, 0.01f, prefer_slider")]
    public float GrassVariationFrequency
    {
        get => _grassVariationFrequency;
        set => _grassVariationFrequency = value;
    }

    [ExportGroup("Tree Settings")]
    private bool _treesEnabled = true;
    [Export] public bool TreesEnabled
    {
        get => _treesEnabled;
        set
        {
            if (_treesEnabled == value)
                return;
            _treesEnabled = value;
            if (_treesEnabled)
            {
                InitializeTreePlacer();
            }
            else
            {
                _treePlacer?.ClearAll();
                _treePlacer = null;
            }
        }
    }

    private MeshInstance3D _treeTemplate;
    [Export] public MeshInstance3D TreeTemplate
    {
        get => _treeTemplate;
        set => _treeTemplate = value;
    }

    private int _treeForestDensity = 10;
    [Export(PropertyHint.Range, "0, 50, 1, prefer_slider")]
    public int TreeForestDensity
    {
        get => _treeForestDensity;
        set => _treeForestDensity = value;
    }

    private int _treeJungleDensity = 20;
    [Export(PropertyHint.Range, "0, 50, 1, prefer_slider")]
    public int TreeJungleDensity
    {
        get => _treeJungleDensity;
        set => _treeJungleDensity = value;
    }

    private float _treeMinHeightThreshold = 0.35f;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.05f, prefer_slider")]
    public float TreeMinHeightThreshold
    {
        get => _treeMinHeightThreshold;
        set => _treeMinHeightThreshold = value;
    }

    private float _treeMaxHeightThreshold = 0.65f;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.05f, prefer_slider")]
    public float TreeMaxHeightThreshold
    {
        get => _treeMaxHeightThreshold;
        set => _treeMaxHeightThreshold = value;
    }

    private float _treeMinScale = 0.8f;
    [Export(PropertyHint.Range, "0.5f, 3.0f, 0.1f, prefer_slider")]
    public float TreeMinScale
    {
        get => _treeMinScale;
        set => _treeMinScale = value;
    }

    private float _treeMaxScale = 1.5f;
    [Export(PropertyHint.Range, "0.5f, 3.0f, 0.1f, prefer_slider")]
    public float TreeMaxScale
    {
        get => _treeMaxScale;
        set => _treeMaxScale = value;
    }

    private int _treeDensityNoiseScale = 50;
    [Export(PropertyHint.Range, "1, 200, 1, prefer_slider")]
    public int TreeDensityNoiseScale
    {
        get => _treeDensityNoiseScale;
        set => _treeDensityNoiseScale = value;
    }

    private int _treeDensityNoiseAmplitude = 5;
    [Export(PropertyHint.Range, "0, 20, 1, prefer_slider")]
    public int TreeDensityNoiseAmplitude
    {
        get => _treeDensityNoiseAmplitude;
        set => _treeDensityNoiseAmplitude = value;
    }

    private int _treeRandomSeedBase = 54321;
    [Export(PropertyHint.Range, "0, 100000, 1, prefer_slider")]
    public int TreeRandomSeedBase
    {
        get => _treeRandomSeedBase;
        set => _treeRandomSeedBase = value;
    }

    private float _treePlacementFrequency = 0.03f;
    [Export(PropertyHint.Range, "0.01f, 0.5f, 0.001f, prefer_slider")]
    public float TreePlacementFrequency
    {
        get => _treePlacementFrequency;
        set => _treePlacementFrequency = value;
    }

    private readonly Dictionary<Vector2I, MeshInstance3D> _chunks = [];
    private readonly Dictionary<Vector2I, MeshInstance3D> _waterMeshes = [];
    private readonly object _chunkLock = new();
    private Node3D _chunkContainer;
    private Vector2I _currentChunkCoord;
    private GrassPlacer _grassPlacer;
    private TreePlacer _treePlacer;
    private Node3D _player;

    private void InitializeGrassPlacer()
    {
        _grassPlacer = new GrassPlacer(
            this,
            this,
            _grasslandDensity,
            _grassForestDensity,
            _grassJungleDensity,
            _grassSavannaDensity,
            _minGrassHeight,
            _maxGrassHeight,
            _grassHeightOffset,
            _renderDistance,
            _grassGridSpacing,
            _grassDensityThreshold,
            _grassPlacementFrequency,
            _grassVariationFrequency);
        _grassPlacer.SetTemplate(_grassTemplate);

        foreach (var coord in _chunks.Keys)
        {
            _grassPlacer.GenerateForChunk(coord);
        }
    }

    private void InitializeTreePlacer()
    {
        _treePlacer = new TreePlacer(
            this,
            this,
            _treeForestDensity,
            _treeJungleDensity,
            _treeMinHeightThreshold,
            _treeMaxHeightThreshold,
            _treeMinScale,
            _treeMaxScale,
            _renderDistance,
            _treeDensityNoiseScale,
            _treeDensityNoiseAmplitude,
            _treeRandomSeedBase,
            _treePlacementFrequency);
        _treePlacer.SetTemplate(_treeTemplate);

        foreach (var coord in _chunks.Keys)
        {
            _treePlacer.GenerateForChunk(coord);
        }
    }

    public override void _Ready()
    {
        _chunkContainer = new Node3D { Name = "ChunkContainer" };
        AddChild(_chunkContainer);

        if (_grassEnabled)
        {
            InitializeGrassPlacer();
        }

        if (_treesEnabled)
        {
            InitializeTreePlacer();
        }

        if (!Engine.IsEditorHint())
        {
            UpdateChunksForPosition(Vector3.Zero);
        }

        _player = GetTree().CurrentScene?.GetNode<Node3D>("Player");
        if (!IsInstanceValid(_player))
        {
            GD.PrintErr(Name, ".", nameof(_Ready), " : ", "Player node not found in current scene.");
        }
    }

    public override void _Process(double delta)
    {
        if (IsInstanceValid(_player))
        {
            UpdateChunksForPosition(_player.GlobalPosition);
        }
    }

    public Vector2I GetChunkCoord(float worldX, float worldZ)
    {
        return new Vector2I((int)Math.Floor(worldX / _chunkSize), (int)Math.Floor(worldZ / _chunkSize));
    }

    public float GetMoisture(float worldX, float worldZ)
    {
        return (_moistureNoise.GetNoise2D(worldX, worldZ) + 1.0f) / 2.0f;
    }

    public float GetTemperature(float worldX, float worldZ)
    {
        return (_temperatureNoise.GetNoise2D(worldX, worldZ) + 1.0f) / 2.0f;
    }

    public float GetHeight(float worldX, float worldZ)
    {
        return _heightNoise.GetNoise2D(worldX, worldZ) * _height;
    }

    public Vector3 GetNormal(float worldX, float worldZ)
    {
        var epsilon = (float)_chunkSize / _resolution;
        var normal = new Vector3(
            (GetHeight(worldX + epsilon, worldZ) - GetHeight(worldX - epsilon, worldZ)) / (2.0f * epsilon),
            1.0f,
            (GetHeight(worldX, worldZ + epsilon) - GetHeight(worldX, worldZ - epsilon)) / (2.0f * epsilon)
        );

        return normal.Normalized();
    }

    public BiomeType GetBiome(float worldX, float worldZ)
    {
        var moisture = GetMoisture(worldX, worldZ);
        var temperature = GetTemperature(worldX, worldZ);
        var height = GetHeight(worldX, worldZ);

        return GetBiome(moisture, temperature, height);
    }

    public BiomeType GetBiome(float moisture, float temperature, float height)
    {
        if (height < _oceanHeightThreshold)
            return BiomeType.Ocean;

        if (height > _mountainHeightThreshold)
        {
            if (temperature < _tundraTemperatureThreshold)
                return BiomeType.Snow;
            return BiomeType.Mountain;
        }

        if (temperature < _tundraTemperatureThreshold)
            return BiomeType.Tundra;

        if (temperature > _desertTemperatureThreshold)
        {
            if (moisture < _sandMoistureThreshold)
                return BiomeType.Sand;
            if (moisture < _desertMoistureThreshold)
                return BiomeType.Desert;
            return BiomeType.Savanna;
        }

        if (moisture < _grasslandMoistureThreshold)
            return BiomeType.Grassland;
        if (moisture < _forestMoistureThreshold)
            return BiomeType.Forest;
        return BiomeType.Jungle;
    }

    public (BiomeType primary, BiomeType secondary, float blendFactor) GetBiomeWithBlend(float moisture, float temperature, float height)
    {
        var biome = GetBiome(moisture, temperature, height);
        var edge = _blendEdgeWidth;

        if (height < _oceanHeightThreshold + edge)
        {
            if (height > _oceanHeightThreshold)
                return (BiomeType.Grassland, BiomeType.Ocean, 1.0f - (height - _oceanHeightThreshold) / edge);
            return (biome, biome, 0.0f);
        }

        if (height > _mountainHeightThreshold - edge && height < _mountainHeightThreshold + edge)
        {
            var dist = height - _mountainHeightThreshold;
            var blendFactor = 1.0f - Math.Abs(dist) / edge;
            if (temperature < _tundraTemperatureThreshold)
                return (BiomeType.Snow, BiomeType.Mountain, blendFactor);
            return (BiomeType.Mountain, BiomeType.Tundra, blendFactor);
        }

        if (temperature < _tundraTemperatureThreshold + edge && temperature > _tundraTemperatureThreshold - edge)
        {
            var dist = temperature - _tundraTemperatureThreshold;
            var blendFactor = 1.0f - Math.Abs(dist) / edge;
            return (BiomeType.Tundra, BiomeType.Forest, blendFactor);
        }

        if (temperature > _desertTemperatureThreshold - edge)
        {
            if (moisture < _sandMoistureThreshold + edge && moisture > _sandMoistureThreshold - edge)
            {
                var dist = moisture - _sandMoistureThreshold;
                var blendFactor = 1.0f - Math.Abs(dist) / edge;
                return (BiomeType.Sand, BiomeType.Desert, blendFactor);
            }
            if (moisture < _desertMoistureThreshold + edge && moisture > _desertMoistureThreshold - edge)
            {
                var dist = moisture - _desertMoistureThreshold;
                var blendFactor = 1.0f - Math.Abs(dist) / edge;
                return (BiomeType.Desert, BiomeType.Savanna, blendFactor);
            }
        }

        if (moisture < _grasslandMoistureThreshold + edge && moisture > _grasslandMoistureThreshold - edge)
        {
            var dist = moisture - _grasslandMoistureThreshold;
            var blendFactor = 1.0f - Math.Abs(dist) / edge;
            return (BiomeType.Grassland, BiomeType.Forest, blendFactor);
        }

        if (moisture < _forestMoistureThreshold + edge && moisture > _forestMoistureThreshold - edge)
        {
            var dist = moisture - _forestMoistureThreshold;
            var blendFactor = 1.0f - Math.Abs(dist) / edge;
            return (BiomeType.Forest, BiomeType.Jungle, blendFactor);
        }

        return (biome, biome, 0.0f);
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
            var (mesh, hasOcean) = GenerateChunkMeshData(capturedCoord);
            CallDeferred(nameof(AssignChunkMesh), capturedCoord, mesh, hasOcean);
        });
        thread.Start();
    }

    private void AssignChunkMesh(Vector2I coord, ArrayMesh mesh, bool hasOcean)
    {
        if (_chunks.TryGetValue(coord, out var chunk))
        {
            chunk.Mesh = mesh;
        }

        if (hasOcean && IsInstanceValid(WaterTemplate))
        {
            CreateWaterMesh(coord);
        }

        _grassPlacer?.GenerateForChunk(coord);
        _treePlacer?.GenerateForChunk(coord);
    }

    private void CreateWaterMesh(Vector2I coord)
    {
        var waterMesh = WaterTemplate.Duplicate() as MeshInstance3D;
        waterMesh.Name = $"Water_{coord.X}_{coord.Y}";
        waterMesh.Position = new Vector3(coord.X * _chunkSize, 0, coord.Y * _chunkSize);
        waterMesh.Visible = true;

        float waterLevel = (_oceanHeightThreshold * 2.0f - 1.0f) * _height;
        waterMesh.Position = new Vector3(coord.X * _chunkSize, waterLevel, coord.Y * _chunkSize);

        _chunkContainer.AddChild(waterMesh);
        _waterMeshes[coord] = waterMesh;
    }

    private (ArrayMesh mesh, bool hasOcean) GenerateChunkMeshData(Vector2I coord)
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
        bool hasOcean = false;

        for (int i = 0; i < vertexArray.Length; i++)
        {
            var vertex = vertexArray[i];
            var worldX = offsetX + vertex.X;
            var worldZ = offsetZ + vertex.Z;

            var heightValue = GetHeight(worldX, worldZ);
            vertex.Y = heightValue;

            var normal = GetNormal(worldX, worldZ);
            var tangent = normal.Cross(Vector3.Up);

            var normalizedHeight = (heightValue / _height + 1.0f) / 2.0f;
            var moisture = GetMoisture(worldX, worldZ);
            var temperature = GetTemperature(worldX, worldZ);

            var biome = GetBiome(moisture, temperature, normalizedHeight);
            if (biome == BiomeType.Ocean)
                hasOcean = true;

            var (primaryBiome, secondaryBiome, blendFactor) = GetBiomeWithBlend(moisture, temperature, normalizedHeight);
            colorArray[i] = new Color((float)primaryBiome / 10.0f, blendFactor, (float)secondaryBiome / 10.0f, 1.0f);

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

        return (arrayMesh, hasOcean);
    }

    private void RemoveChunk(Vector2I coord)
    {
        if (_chunks.TryGetValue(coord, out var chunk))
        {
            chunk.QueueFree();
            _chunks.Remove(coord);
        }

        if (_waterMeshes.TryGetValue(coord, out var waterMesh))
        {
            waterMesh.QueueFree();
            _waterMeshes.Remove(coord);
        }

        _grassPlacer?.RemoveForChunk(coord);
        _treePlacer?.RemoveForChunk(coord);
    }

    private void RegenerateAllChunks()
    {
        List<(Vector2I coord, MeshInstance3D mesh)> chunksToUpdate;
        lock (_chunkLock)
        {
            chunksToUpdate = [.. _chunks.Select(kv => (kv.Key, kv.Value))];
        }

        foreach (var coord in _waterMeshes.Keys.ToList())
        {
            if (_waterMeshes.TryGetValue(coord, out var waterMesh))
            {
                waterMesh.QueueFree();
                _waterMeshes.Remove(coord);
            }
        }

        var threads = new List<Thread>();
        var pendingMeshes = new ConcurrentDictionary<Vector2I, (ArrayMesh mesh, bool hasOcean)>();

        foreach (var (coord, chunk) in chunksToUpdate)
        {
            var capturedCoord = coord;
            var thread = new Thread(() =>
            {
                var result = GenerateChunkMeshData(capturedCoord);
                pendingMeshes[capturedCoord] = result;
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
                    chunk.Mesh = kvp.Value.mesh;
                }

                if (kvp.Value.hasOcean && IsInstanceValid(WaterTemplate))
                {
                    CreateWaterMesh(kvp.Key);
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
}
