using System.Collections.Generic;
using Godot;

namespace AdventureGame.Scripts;

public partial class WaterGenerator : Resource
{
    [Export] public PlaneMesh WaterMesh { get; set; }

    private TerrainController _terrain;
    private readonly Dictionary<Vector2I, MeshInstance3D> _waterMeshes = [];
    private readonly object _lock = new();

    public void Initialize(TerrainController terrain)
    {
        ClearAll();
        _terrain = terrain;

        if (!IsInstanceValid(WaterMesh))
        {
            GD.PushWarning($"{nameof(WaterMesh)} is not assigned.");
        }
    }

    public void GenerateForChunk(Vector2I coord)
    {
        if (!IsInstanceValid(WaterMesh))
            return;

        if (!ShouldGenerateForChunk(coord))
            return;

        var waterMesh = new MeshInstance3D
        {
            Mesh = WaterMesh,
            Name = $"Water_{coord.X}_{coord.Y}"
        };

        float waterLevel = _terrain.GetWaterLevel();
        waterMesh.Position = _terrain.GetWorldCoord(coord) + new Vector3(0, waterLevel, 0);
        waterMesh.Visible = true;

        lock (_lock)
        {
            if (_waterMeshes.TryGetValue(coord, out var existing))
            {
                if (IsInstanceValid(existing))
                    existing.QueueFree();
                _waterMeshes.Remove(coord);
            }
            _waterMeshes[coord] = waterMesh;
        }

        _terrain.ChunkContainer.AddChild(waterMesh);
    }

    public void RemoveForChunk(Vector2I coord)
    {
        lock (_lock)
        {
            if (_waterMeshes.TryGetValue(coord, out var waterMesh))
            {
                if (IsInstanceValid(waterMesh))
                    waterMesh.QueueFree();
                _waterMeshes.Remove(coord);
            }
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            foreach (var waterMesh in _waterMeshes.Values)
            {
                if (IsInstanceValid(waterMesh))
                    waterMesh.QueueFree();
            }
            _waterMeshes.Clear();
        }
    }

    private bool ShouldGenerateForChunk(Vector2I coord)
    {
        var chunkSize = _terrain.ChunkSize;
        var resolution = _terrain.Resolution;
        var step = chunkSize / (float)resolution;
        var offsetX = coord.X * chunkSize;
        var offsetZ = coord.Y * chunkSize;
        var height = _terrain.Height;
        var oceanThreshold = _terrain.OceanHeightThreshold;

        for (float x = 0; x <= chunkSize; x += step)
            for (float z = 0; z <= chunkSize; z += step)
            {
                var heightMap = _terrain.GetHeightmap(offsetX + x, offsetZ + z);
                if ((heightMap / height + 1.0f) * 0.5f < oceanThreshold)
                    return true;
            }

        return false;
    }
}
