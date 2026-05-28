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
    Mountain
}

public partial class TerrainController : Node3D
{
    [ExportGroup("Noise Settings")]

    [Export] public FastNoiseLite TerrainNoise { get; set; }
    [Export] public FastNoiseLite ErosionNoise { get; set; }
    [Export] public FastNoiseLite RnrNoise { get; set; }
    [Export] public FastNoiseLite TemperatureNoise { get; set; }
    [Export] public FastNoiseLite MoistureNoise { get; set; }
    [Export] public FastNoiseLite DetailNoise { get; set; }
    [Export] public FastNoiseLite ClutterNoise { get; set; }

    [ExportGroup("Biome Generators")]

    [Export] public OceanScript OceanGenerator { get; set; }
    [Export] public PlainsScript PlainsGenerator { get; set; }
    [Export] public ForestScript ForestGenerator { get; set; }
    [Export] public MountainScript MountainGenerator { get; set; }

    [ExportGroup("Chunk Settings")]

    [Export] public MeshInstance3D ChunkTemplate { get; set; }
    [Export] public MeshInstance3D WaterTemplate { get; set; }
    [Export] public int ChunkSize { get; set; } = 64;
    [Export(PropertyHint.Range, "0, 24, 1, prefer_slider")]
    public int RenderDistance { get; set; } = 4;
    [Export(PropertyHint.Range, "4, 256, 4, prefer_slider")]
    public int Resolution { get; set; } = 32;
    [Export(PropertyHint.Range, "4.0f, 256.0f, 4.0f, prefer_slider")]
    public float Height { get; set; } = 64.0f;
    [Export(PropertyHint.Range, "0.0, 0.5, 0.01, prefer_slider")]
    public float BiomeBlendWidth { get; set; } = 0.1f;
    [Export(PropertyHint.Range, "0, 10, 0.1, prefer_slider")]
    public float PlayerOffset { get; set; } = 0.0f;

    [ExportGroup("Biome Thresholds")]

    [Export(PropertyHint.Range, "0.0, 1.0, 0.05, prefer_slider")]
    public float OceanHeightThreshold { get; set; } = 0.4f;
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05, prefer_slider")]
    public float MountainHeightThreshold { get; set; } = 0.7f;
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05, prefer_slider")]
    public float GrasslandMoistureThreshold { get; set; } = 0.3f;
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05, prefer_slider")]
    public float ForestMoistureThreshold { get; set; } = 0.6f;

    [ExportGroup("Decoration Settings")]

    [Export] public GrassPlacer GrassPlacer { get; set; }
    [Export] public MeshInstance3D GrassTemplate { get; set; }
    [Export] public bool GrassEnabled { get; set; } = true;
    [Export] public TreePlacer TreePlacer { get; set; }
    [Export] public MeshInstance3D TreeTemplate { get; set; }
    [Export] public bool TreesEnabled { get; set; } = true;

    public Vector2I CurrentChunkCoord { get; private set; }

    private Node3D _chunkContainer;
    private Node3D _player;

    private readonly Dictionary<Vector2I, MeshInstance3D> _chunks = [];
    private readonly Dictionary<Vector2I, MeshInstance3D> _waterMeshes = [];
    private readonly object _chunkLock = new();

    private bool ValidateNoiseSettings()
    {
        if (!IsInstanceValid(TerrainNoise))
        {
            GD.PrintErr($"{Name}._Ready : TerrainNoise is not assigned.");
            return false;
        }
        if (!IsInstanceValid(ErosionNoise))
        {
            GD.PrintErr($"{Name}._Ready : ErosionNoise is not assigned.");
            return false;
        }
        if (!IsInstanceValid(RnrNoise))
        {
            GD.PrintErr($"{Name}._Ready : RnrNoise is not assigned.");
            return false;
        }
        if (!IsInstanceValid(TemperatureNoise))
        {
            GD.PrintErr($"{Name}._Ready : TemperatureNoise is not assigned.");
            return false;
        }
        if (!IsInstanceValid(MoistureNoise))
        {
            GD.PrintErr($"{Name}._Ready : MoistureNoise is not assigned.");
            return false;
        }
        if (!IsInstanceValid(DetailNoise))
        {
            GD.PrintErr($"{Name}._Ready : DetailNoise is not assigned.");
            return false;
        }
        if (!IsInstanceValid(ClutterNoise))
        {
            GD.PrintErr($"{Name}._Ready : ClutterNoise is not assigned.");
            return false;
        }

        return true;
    }

    private bool ValidateBiomeGenerators()
    {
        if (!IsInstanceValid(OceanGenerator))
        {
            GD.PrintErr($"{Name}._Ready : OceanGenerator is not assigned.");
            return false;
        }
        if (!IsInstanceValid(PlainsGenerator))
        {
            GD.PrintErr($"{Name}._Ready : PlainsGenerator is not assigned.");
            return false;
        }
        if (!IsInstanceValid(ForestGenerator))
        {
            GD.PrintErr($"{Name}._Ready : ForestGenerator is not assigned.");
            return false;
        }
        if (!IsInstanceValid(MountainGenerator))
        {
            GD.PrintErr($"{Name}._Ready : MountainGenerator is not assigned.");
            return false;
        }

        return true;
    }

    private BiomeGenerator GetBiomeGenerator(BiomeType biome)
    {
        return biome switch
        {
            BiomeType.Ocean => OceanGenerator,
            BiomeType.Grassland => PlainsGenerator,
            BiomeType.Forest => ForestGenerator,
            BiomeType.Mountain => MountainGenerator,
            _ => ForestGenerator
        };
    }

    private void InitializeGrassPlacer()
    {
        if (!IsInstanceValid(GrassPlacer))
            return;

        GrassPlacer.Initialize(this);

        foreach (var coord in _chunks.Keys)
        {
            GrassPlacer.GenerateForChunk(coord);
        }
    }

    private void InitializeTreePlacer()
    {
        if (!IsInstanceValid(TreePlacer))
            return;

        TreePlacer.Initialize(this);

        foreach (var coord in _chunks.Keys)
        {
            TreePlacer.GenerateForChunk(coord);
        }
    }

    public override void _Ready()
    {
        _chunkContainer = new Node3D { Name = "ChunkContainer" };
        AddChild(_chunkContainer);

        if (!ValidateNoiseSettings())
        {
            QueueFree();
            return;
        }

        if (!ValidateBiomeGenerators())
        {
            QueueFree();
            return;
        }

        _player = GetTree().CurrentScene?.GetNodeOrNull<Node3D>("Player");
        if (!IsInstanceValid(_player))
        {
            GD.PrintErr($"{Name}.{nameof(_Ready)} : Player node not found in current scene.");
            QueueFree();
            return;
        }

        if (GrassEnabled)
            InitializeGrassPlacer();

        if (TreesEnabled)
            InitializeTreePlacer();

        CurrentChunkCoord = GetChunkCoord(_player.GlobalPosition);
        LoadChunksAroundPlayer();
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
        return new Vector2I((int)Math.Floor(worldX / ChunkSize), (int)Math.Floor(worldZ / ChunkSize));
    }

    public Vector2I GetChunkCoord(Vector3 position)
    {
        return GetChunkCoord(position.X, position.Z);
    }

    public Vector3 GetWorldCoord(Vector2I coord)
    {
        var worldCoord = coord * ChunkSize;
        var worldOffset = ChunkSize / 2;
        return new Vector3I(worldCoord.X + worldOffset, 0, worldCoord.Y + worldOffset);
    }

    public float GetMoisture(float worldX, float worldZ)
    {
        return (MoistureNoise.GetNoise2D(worldX, worldZ) + 1.0f) / 2.0f;
    }

    public float GetTemperature(float worldX, float worldZ)
    {
        return (TemperatureNoise.GetNoise2D(worldX, worldZ) + 1.0f) / 2.0f;
    }

    public float GetBaseHeight(float worldX, float worldZ)
    {
        var continental = TerrainNoise.GetNoise2D(worldX, worldZ);
        var rivers = RnrNoise.GetNoise2D(worldX, worldZ);
        return Mathf.Clamp(continental + rivers, -1.0f, 1.0f) * Height;
    }

    public float GetHeightmap(float worldX, float worldZ)
    {
        var baseHeight = GetBaseHeight(worldX, worldZ);

        var normalizedHeight = (baseHeight / Height + 1.0f) * 0.5f;
        var moisture = GetMoisture(worldX, worldZ);
        var temperature = GetTemperature(worldX, worldZ);
        var biome = GetBiome(moisture, temperature, normalizedHeight);
        var generator = GetBiomeGenerator(biome);

        var biomeHeight = generator.HeightmapNoise.GetNoise2D(worldX, worldZ) * Height * 0.2f;
        var erosionWeight = generator.ErosionWeight?.Sample(0.5f) ?? 1.0f;
        var detailWeight = generator.DetailWeight?.Sample(0.5f) ?? 1.0f;

        var height = baseHeight + biomeHeight;
        height *= 1.0f - Mathf.Max(0.0f, ErosionNoise.GetNoise2D(worldX, worldZ)) * erosionWeight * 0.5f;
        height += DetailNoise.GetNoise2D(worldX, worldZ) * detailWeight * 0.1f;

        return height;
    }

    public Vector3 GetNormal(float worldX, float worldZ)
    {
        var epsilon = (float)ChunkSize / Resolution;
        var normal = new Vector3(
            (GetHeightmap(worldX + epsilon, worldZ) - GetHeightmap(worldX - epsilon, worldZ)) / (2.0f * epsilon),
            1.0f,
            (GetHeightmap(worldX, worldZ + epsilon) - GetHeightmap(worldX, worldZ - epsilon)) / (2.0f * epsilon)
        );

        return normal.Normalized();
    }

    public BiomeType GetBiome(float worldX, float worldZ)
    {
        var moisture = GetMoisture(worldX, worldZ);
        var temperature = GetTemperature(worldX, worldZ);
        var height = GetBaseHeight(worldX, worldZ);
        var normalizedHeight = (height / Height + 1.0f) / 2.0f;

        return GetBiome(moisture, temperature, normalizedHeight);
    }

    public BiomeType GetBiome(float moisture, float temperature, float height)
    {
        if (height < OceanHeightThreshold)
            return BiomeType.Ocean;

        if (height > MountainHeightThreshold)
            return BiomeType.Mountain;

        if (moisture > ForestMoistureThreshold)
            return BiomeType.Forest;

        return BiomeType.Grassland;
    }

    public (BiomeType primary, BiomeType secondary, float blendFactor) GetBiomeWithBlend(float moisture, float temperature, float height)
    {
        var biome = GetBiome(moisture, temperature, height);
        var edge = BiomeBlendWidth;

        if (height < OceanHeightThreshold + edge)
        {
            if (height > OceanHeightThreshold)
                return (BiomeType.Grassland, BiomeType.Ocean, 1.0f - (height - OceanHeightThreshold) / edge);
            return (biome, biome, 0.0f);
        }

        if (height > MountainHeightThreshold - edge && height < MountainHeightThreshold + edge)
        {
            var dist = height - MountainHeightThreshold;
            var blendFactor = 1.0f - Math.Abs(dist) / edge;
            return (BiomeType.Mountain, BiomeType.Forest, blendFactor);
        }

        if (moisture < GrasslandMoistureThreshold + edge && moisture > GrasslandMoistureThreshold - edge)
        {
            var dist = moisture - GrasslandMoistureThreshold;
            var blendFactor = 1.0f - Math.Abs(dist) / edge;
            return (BiomeType.Grassland, BiomeType.Forest, blendFactor);
        }

        if (moisture < ForestMoistureThreshold + edge && moisture > ForestMoistureThreshold - edge)
        {
            var dist = moisture - ForestMoistureThreshold;
            var blendFactor = 1.0f - Math.Abs(dist) / edge;
            return (BiomeType.Forest, BiomeType.Grassland, blendFactor);
        }

        return (biome, biome, 0.0f);
    }

    private void UpdateChunksForPosition(Vector3 position)
    {
        var newChunkCoord = GetChunkCoord(position.X, position.Z);

        if (newChunkCoord == CurrentChunkCoord)
            return;

        CurrentChunkCoord = newChunkCoord;
        LoadChunksAroundPlayer();
    }

    private void LoadChunksAroundPlayer()
    {
        var neededChunks = new HashSet<Vector2I>();

        for (int x = -RenderDistance; x <= RenderDistance; x++)
        {
            for (int z = -RenderDistance; z <= RenderDistance; z++)
            {
                var coord = new Vector2I(CurrentChunkCoord.X + x, CurrentChunkCoord.Y + z);
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
        var worldCoord = GetWorldCoord(coord);

        var chunkMesh = IsInstanceValid(ChunkTemplate)
            ? ChunkTemplate.Duplicate() as MeshInstance3D
            : new MeshInstance3D();

        chunkMesh.Name = $"Chunk_{coord.X}_{coord.Y}";
        chunkMesh.Position = worldCoord;
        chunkMesh.Visible = true;

        _chunkContainer.AddChild(chunkMesh);
        _chunks[coord] = chunkMesh;

        var thread = new Thread(() =>
        {
            var (mesh, hasOcean) = GenerateChunkMeshData(worldCoord);
            CallDeferred(nameof(AssignChunkMesh), coord, mesh, hasOcean);
        });
        thread.Start();
    }

    private void AssignChunkMesh(Vector2I coord, ArrayMesh mesh, bool hasOcean)
    {
        lock (_chunkLock)
        {
            if (!_chunks.TryGetValue(coord, out var chunk))
                return;
            chunk.Mesh = mesh;
        }

        PlaceDecorations(coord, hasOcean);
    }

    private void PlaceDecorations(Vector2I coord, bool hasOcean)
    {
        if (hasOcean && IsInstanceValid(WaterTemplate))
            CreateWaterMesh(coord);

        if (GrassEnabled && IsInstanceValid(GrassPlacer))
            GrassPlacer.GenerateForChunk(coord);

        if (TreesEnabled && IsInstanceValid(TreePlacer))
            TreePlacer.GenerateForChunk(coord);
    }

    private void CreateWaterMesh(Vector2I coord)
    {
        var waterMesh = WaterTemplate.Duplicate() as MeshInstance3D;
        waterMesh.Name = $"Water_{coord.X}_{coord.Y}";
        waterMesh.Position = new Vector3(coord.X * ChunkSize, 0, coord.Y * ChunkSize);
        waterMesh.Visible = true;

        float waterLevel = (OceanHeightThreshold * 2.0f - 1.0f) * Height;
        waterMesh.Position = GetWorldCoord(coord) + new Vector3(0, waterLevel, 0);

        _chunkContainer.AddChild(waterMesh);
        _waterMeshes[coord] = waterMesh;
    }

    private (ArrayMesh mesh, bool hasOcean) GenerateChunkMeshData(Vector3 position)
    {
        var plane = new PlaneMesh
        {
            SubdivideDepth = Resolution,
            SubdivideWidth = Resolution,
            Size = new Vector2(ChunkSize, ChunkSize)
        };

        var planeArrays = plane.GetMeshArrays();

        var vertexArray = planeArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normalArray = planeArrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var tangentArray = planeArrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
        var colorArray = new Color[vertexArray.Length];

        bool hasOcean = false;

        for (int i = 0; i < vertexArray.Length; i++)
        {
            var vertex = vertexArray[i];
            var worldX = position.X + vertex.X;
            var worldZ = position.Z + vertex.Z;

            var heightValue = GetHeightmap(worldX, worldZ);
            vertex.Y = heightValue;

            var normal = GetNormal(worldX, worldZ);
            var tangent = normal.Cross(Vector3.Up);

            var normalizedHeight = (heightValue / Height + 1.0f) / 2.0f;
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

        if (GrassEnabled && IsInstanceValid(GrassPlacer))
            GrassPlacer.RemoveForChunk(coord);

        if (TreesEnabled && IsInstanceValid(TreePlacer))
            TreePlacer.RemoveForChunk(coord);
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
            var worldCoord = GetWorldCoord(coord);
            var thread = new Thread(() =>
            {
                var result = GenerateChunkMeshData(worldCoord);
                pendingMeshes[coord] = result;
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

        if (GrassEnabled && IsInstanceValid(GrassPlacer))
        {
            GrassPlacer.ClearAll();
            foreach (var coord in _chunks.Keys)
            {
                GrassPlacer.GenerateForChunk(coord);
            }
        }

        if (TreesEnabled && IsInstanceValid(TreePlacer))
        {
            TreePlacer.ClearAll();
            foreach (var coord in _chunks.Keys)
            {
                TreePlacer.GenerateForChunk(coord);
            }
        }
    }
}
