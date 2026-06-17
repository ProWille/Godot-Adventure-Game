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
    [Export] public BiomeGenerator OceanGenerator { get; set; }
    [Export] public BiomeGenerator PlainsGenerator { get; set; }
    [Export] public BiomeGenerator ForestGenerator { get; set; }
    [Export] public BiomeGenerator MountainGenerator { get; set; }

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
    [Export] public DecorationGenerator TreeGenerator { get; set; }
    [Export] public MeshInstance3D TreeTemplate { get; set; }
    [Export] public bool TreesEnabled { get; set; } = true;
    [Export] public DecorationGenerator GrassGenerator { get; set; }
    [Export] public MeshInstance3D GrassTemplate { get; set; }
    [Export] public bool GrassEnabled { get; set; } = true;
    [Export] public DecorationGenerator StoneGenerator { get; set; }
    [Export] public MeshInstance3D StoneTemplate { get; set; }
    [Export] public bool StonesEnabled { get; set; } = true;

    [ExportGroup("Player Settings")]
    [Export] public Node3D Player { get; set; }
    [Export] public Node3D Camera { get; set; }
    [Export] public bool IsPlayerActive { get; set; } = true;

    public Vector2I CurrentChunkCoord { get; private set; }
    public Node3D ActivePlayer => IsPlayerActive ? Player : Camera;

    private Camera3D PlayerCam => Player?.GetNodeOrNull<Camera3D>("CameraPivot/Camera3D");
    private Camera3D FreeCam => Camera as Camera3D;
    private Node3D _chunkContainer;

    private readonly Dictionary<Vector2I, MeshInstance3D> _chunks = [];
    private readonly Dictionary<Vector2I, MeshInstance3D> _waterMeshes = [];
    private readonly object _chunkLock = new();

    public float GetWaterLevel() => (OceanHeightThreshold * 2.0f - 1.0f) * Height;
    public float GetMoisture(float worldX, float worldZ) => (MoistureNoise.GetNoise2D(worldX, worldZ) + 1.0f) * 0.5f;
    public float GetTemperature(float worldX, float worldZ) => (TemperatureNoise.GetNoise2D(worldX, worldZ) + 1.0f) * 0.5f;
    public float GetNormalizedHeight(float baseHeight) => (baseHeight / Height + 1.0f) * 0.5f;
    public float GetNormalizedHeight(float worldX, float worldZ) => GetNormalizedHeight(GetBaseHeight(worldX, worldZ));
    public Vector2I GetChunkCoord(float worldX, float worldZ) => new((int)Math.Floor(worldX / ChunkSize), (int)Math.Floor(worldZ / ChunkSize));
    public Vector2I GetChunkCoord(Vector3 position) => GetChunkCoord(position.X, position.Z);
    public Vector3 GetWorldCoord(Vector2I coord) => new(coord.X * ChunkSize + ChunkSize / 2, 0, coord.Y * ChunkSize + ChunkSize / 2);

    private static bool ValidateFields(params (GodotObject field, string name)[] fields)
    {
        foreach (var (field, name) in fields)
        {
            if (!IsInstanceValid(field))
            {
                GD.PushError($"{name} is not assigned.");
                return false;
            }
        }
        return true;
    }

    private bool ValidateNoiseSettings() => ValidateFields(
        (TerrainNoise, nameof(TerrainNoise)),
        (ErosionNoise, nameof(ErosionNoise)),
        (RnrNoise, nameof(RnrNoise)),
        (TemperatureNoise, nameof(TemperatureNoise)),
        (MoistureNoise, nameof(MoistureNoise)),
        (DetailNoise, nameof(DetailNoise)),
        (ClutterNoise, nameof(ClutterNoise))
    );

    private bool ValidateBiomeGenerators() => ValidateFields(
        (OceanGenerator, nameof(OceanGenerator)),
        (PlainsGenerator, nameof(PlainsGenerator)),
        (ForestGenerator, nameof(ForestGenerator)),
        (MountainGenerator, nameof(MountainGenerator))
    );

    private void InitializeDecorationGenerators(params (DecorationGenerator generator, bool enabled, string name)[] fields)
    {
        foreach (var (generator, enabled, name) in fields)
        {
            if (!enabled) continue;
            if (!IsInstanceValid(generator))
            {
                GD.PushWarning($"{name} is not assigned.");
                continue;
            }
            generator.Initialize(this);
            foreach (var coord in _chunks.Keys)
                generator.GenerateForChunk(coord);
        }
    }

    private void SetActivePlayer(bool player)
    {
        IsPlayerActive = player;

        if (IsInstanceValid(PlayerCam))
            PlayerCam.Current = IsPlayerActive;
        if (IsInstanceValid(FreeCam))
            FreeCam.Current = !IsPlayerActive;

        if (IsInstanceValid(Player))
            Player.ProcessMode = IsPlayerActive ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        if (IsInstanceValid(Camera))
            Camera.ProcessMode = !IsPlayerActive ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
    }

    public override void _Ready()
    {
        _chunkContainer = new Node3D { Name = "ChunkContainer" };
        AddChild(_chunkContainer);

        if (!ValidateNoiseSettings() || !ValidateBiomeGenerators())
        {
            QueueFree();
            return;
        }

        if (!IsInstanceValid(Player) && !IsInstanceValid(Camera))
        {
            GD.PushError("Player node and Camera node are not assigned.");
            QueueFree();
            return;
        }

        SetActivePlayer(IsPlayerActive);

        InitializeDecorationGenerators((TreeGenerator, TreesEnabled, nameof(TreeGenerator)),
                                       (GrassGenerator, GrassEnabled, nameof(GrassGenerator)),
                                       (StoneGenerator, StonesEnabled, nameof(StoneGenerator)));

        CurrentChunkCoord = GetChunkCoord(ActivePlayer.GlobalPosition);
        LoadChunksAroundPlayer();
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("toggle_player"))
            SetActivePlayer(!IsPlayerActive);

        if (IsInstanceValid(ActivePlayer))
            UpdateChunksForPosition(ActivePlayer.GlobalPosition);
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

        var normalizedHeight = GetNormalizedHeight(baseHeight);
        var moisture = GetMoisture(worldX, worldZ);
        var temperature = GetTemperature(worldX, worldZ);
        var biome = GetBiome(moisture, temperature, normalizedHeight);
        var generator = GetBiomeGenerator(biome);

        var biomeHeight = (generator.HeightmapNoise?.GetNoise2D(worldX, worldZ) ?? 0.0f) * Height * 0.2f;
        var erosionWeight = generator.ErosionWeight?.Sample(normalizedHeight) ?? 1.0f;
        var detailWeight = generator.DetailWeight?.Sample(normalizedHeight) ?? 1.0f;

        var height = baseHeight + biomeHeight;
        height *= 1.0f - Mathf.Max(0.0f, ErosionNoise.GetNoise2D(worldX, worldZ)) * erosionWeight * 0.5f;
        height += DetailNoise.GetNoise2D(worldX, worldZ) * detailWeight * 0.1f;

        return height;
    }

    public float GetBlendedHeightmap(float worldX, float worldZ)
    {
        var baseHeight = GetBaseHeight(worldX, worldZ);

        var normalizedHeight = GetNormalizedHeight(baseHeight);
        var moisture = GetMoisture(worldX, worldZ);
        var temperature = GetTemperature(worldX, worldZ);
        var (primaryBiome, secondaryBiome, blendFactor) = GetBiomeWithBlend(moisture, temperature, normalizedHeight);

        var primaryGenerator = GetBiomeGenerator(primaryBiome);
        var primaryBiomeHeight = (primaryGenerator.HeightmapNoise?.GetNoise2D(worldX, worldZ) ?? 0.0f) * Height * 0.2f;
        var primaryErosionWeight = primaryGenerator.ErosionWeight?.Sample(normalizedHeight) ?? 1.0f;
        var primaryDetailWeight = primaryGenerator.DetailWeight?.Sample(normalizedHeight) ?? 1.0f;

        float biomeHeight;
        float erosionWeight;
        float detailWeight;

        if (blendFactor > 0.001f && secondaryBiome != primaryBiome)
        {
            var secondaryGenerator = GetBiomeGenerator(secondaryBiome);
            var secondaryBiomeHeight = (secondaryGenerator.HeightmapNoise?.GetNoise2D(worldX, worldZ) ?? 0.0f) * Height * 0.2f;
            var secondaryErosionWeight = secondaryGenerator.ErosionWeight?.Sample(normalizedHeight) ?? 1.0f;
            var secondaryDetailWeight = secondaryGenerator.DetailWeight?.Sample(normalizedHeight) ?? 1.0f;

            biomeHeight = Mathf.Lerp(primaryBiomeHeight, secondaryBiomeHeight, blendFactor);
            erosionWeight = Mathf.Lerp(primaryErosionWeight, secondaryErosionWeight, blendFactor);
            detailWeight = Mathf.Lerp(primaryDetailWeight, secondaryDetailWeight, blendFactor);
        }
        else
        {
            biomeHeight = primaryBiomeHeight;
            erosionWeight = primaryErosionWeight;
            detailWeight = primaryDetailWeight;
        }

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

    private Vector3 GetBlendedNormal(float worldX, float worldZ)
    {
        var epsilon = (float)ChunkSize / Resolution;
        var normal = new Vector3(
            (GetBlendedHeightmap(worldX + epsilon, worldZ) - GetBlendedHeightmap(worldX - epsilon, worldZ)) / (2.0f * epsilon),
            1.0f,
            (GetBlendedHeightmap(worldX, worldZ + epsilon) - GetBlendedHeightmap(worldX, worldZ - epsilon)) / (2.0f * epsilon)
        );

        return normal.Normalized();
    }

    public BiomeType GetBiome(float worldX, float worldZ)
    {
        var moisture = GetMoisture(worldX, worldZ);
        var temperature = GetTemperature(worldX, worldZ);
        var height = GetNormalizedHeight(worldX, worldZ);

        return GetBiome(moisture, temperature, height);
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
            var subMountainBiome = moisture > ForestMoistureThreshold ? BiomeType.Forest : BiomeType.Grassland;
            return (BiomeType.Mountain, subMountainBiome, blendFactor);
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

    private static void TryGenerateDecorations(Vector2I coord, params (DecorationGenerator generator, bool enabled)[] fields)
    {
        foreach (var (generator, enabled) in fields)
        {    
            if (!enabled || !IsInstanceValid(generator)) continue;
            generator.GenerateForChunk(coord);
        }
    }

    private void AssignChunkMesh(Vector2I coord, ArrayMesh mesh, bool hasOcean)
    {
        lock (_chunkLock)
        {
            if (!_chunks.TryGetValue(coord, out var chunk))
                return;
            chunk.Mesh = mesh;
        }

        if (hasOcean && IsInstanceValid(WaterTemplate))
            CreateWaterMesh(coord);

        TryGenerateDecorations(coord, (TreeGenerator, TreesEnabled), (GrassGenerator, GrassEnabled), (StoneGenerator, StonesEnabled));
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

            var heightValue = GetBlendedHeightmap(worldX, worldZ);
            vertex.Y = heightValue;

            var normal = GetBlendedNormal(worldX, worldZ);
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

    private static void TryRemoveDecorations(Vector2I coord, params (DecorationGenerator generator, bool enabled)[] fields)
    {
        foreach (var (generator, enabled) in fields)
        {    
            if (!enabled || !IsInstanceValid(generator)) continue;
            generator.RemoveForChunk(coord);
        }
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

        TryRemoveDecorations(coord, (TreeGenerator, TreesEnabled), (GrassGenerator, GrassEnabled), (StoneGenerator, StonesEnabled));
    }

    private void TryRegenerateDecorations(params (DecorationGenerator generator, bool enabled)[] fields)
    {
        foreach (var (generator, enabled) in fields)
        {
            if (!enabled || !IsInstanceValid(generator)) continue;
            generator.ClearAll();
            foreach (var coord in _chunks.Keys)
                generator.GenerateForChunk(coord);
        }
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

        TryRegenerateDecorations((TreeGenerator, TreesEnabled), (GrassGenerator, GrassEnabled), (StoneGenerator, StonesEnabled));
    }
}
