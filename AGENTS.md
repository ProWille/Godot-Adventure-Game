# AGENTS.md - Adventure Game Development Guide

## Project Overview

Godot 4.x C# adventure game with chunk-based procedural terrain generation using FastNoiseLite for biome generation.

## Project Structure

```
/home/william/Development/Godot/Adventure-Game/
├── scripts/                     # C# source files
│   ├── TerrainController.cs     # Main terrain/chunk system
│   ├── CameraController.cs      # Camera controls
│   ├── DebugOverlay.cs          # Debug HUD (FPS, position, biome)
│   ├── PlayerController.cs      # Player movement
│   ├── Biomes/                  # Biome generator resources
│   │   ├── BiomeGenerator.cs    # Base class (HeightmapNoise, DecorationNoise, DetailWeight, ErosionWeight)
│   │   ├── Ocean/OceanScript.cs
│   │   ├── Plains/PlainsScript.cs
│   │   ├── Forest/ForestScript.cs
│   │   └── Mountain/MountainScript.cs
│   └── Decorations/
│       ├── Grass/GrassPlacer.cs # Grass placement (Grassland + Forest biomes)
│       └── Tress/TreePlacer.cs  # Tree placement (Forest biome only)
├── scenes/                      # Godot scene files (.tscn)
│   ├── main.tscn
│   ├── player.tscn
│   ├── camera.tscn
│   ├── sky.tscn
│   ├── test.tscn
│   └── kitty.tscn
├── shaders/                     # GLSL shaders
│   └── terrain.gdshader
├── project.godot
├── Adventure-Game.csproj
└── Adventure-Game.sln
```

## Build Commands

### Build the project
```bash
dotnet build
```

### Open in Godot
```bash
godot --path .
```

### Run the game
```bash
godot --path . --quit
```

### Code Analysis
```bash
# Check for errors
dotnet build --no-restore
```

## Code Style Guidelines

### General Principles
- **File-scoped namespaces** - Use `namespace AdventureGame.Scripts;` without braces
- **Partial classes** - Godot scripts use `public partial class`
- **Tool attribute** - Use `[Tool]` for editor-only scripts that run in the editor

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Private fields | `_camelCase` | `_speed`, `_chunkSize` |
| Public properties | PascalCase | `Speed`, `ChunkSize` |
| Methods | PascalCase | `GetHeight()`, `GenerateChunkMesh()` |
| Classes | PascalCase | `TerrainController`, `GrassPlacer` |
| Enums | PascalCase | `BiomeType.Ocean` |
| Constants | PascalCase | `MaxHeight`, `DefaultChunkSize` |
| Files | PascalCase | `TerrainController.cs` |

### Export Pattern
Always use this pattern for Godot exports:

```csharp
[Export] private float _speed = 15.0f;
public float Speed => _speed;

[Export(PropertyHint.Range, "1, 100, 1, prefer_slider")]
private int _resolution = 32;
public int Resolution
{
    get => _resolution;
    set
    {
        _resolution = Math.Clamp(value, 1, 100);
        RegenerateAllChunks();
    }
}
```

### Import Order
```csharp
using System;
using System.Collections.Generic;
using Godot;
```

### Types and Initialization

- **Use explicit types** for Godot types: `float`, `int`, `Vector3`, `Color`, etc.
- **Prefer `new()` for collections**:
```csharp
private readonly Dictionary<Vector2I, MeshInstance3D> _chunks = [];
private readonly List<Vector3> _positions = new();
```
- **Use `IsInstanceValid()`** to check for null/invalid Godot objects

### Error Handling

- **Logging**: Use `GD.Print()` for info, `GD.PrintErr()` for errors
- **Null checks**: Use `IsInstanceValid(obj)` for Godot objects
- **Try pattern** for dictionaries:
```csharp
if (_chunks.TryGetValue(coord, out var chunk))
    chunk.QueueFree();
```

### Threading

- Use `System.Threading` for background generation
- Use `CallDeferred()` to run code on main thread:
```csharp
var thread = new Thread(() =>
{
    var mesh = GenerateChunkMeshData(coord);
    CallDeferred(nameof(AssignChunkMesh), coord, mesh);
});
thread.Start();
```

### Godot-Specific Patterns

- **Node3D** for 3D objects
- **MultiMeshInstance3D** for instanced rendering (grass, trees)
- **FastNoiseLite** for procedural generation
- **MeshInstance3D** with custom ArrayMesh for terrain
- **Shader parameters** via `SetInstanceShaderParameter()`

## Biome System

The terrain uses three noise types:
- **Height noise** (`TerrainNoise` + `RnrNoise`): Terrain elevation
- **Moisture noise**: Water/humidity levels (0-1 normalized)
- **Temperature noise**: Temperature levels (0-1 normalized)

### Biomes (4-current)
Determined by height and moisture:

| Biome | Conditions |
|-------|-----------|
| Ocean | `height < OceanHeightThreshold` (default 0.4) |
| Grassland | `height` 0.4–0.7, `moisture < ForestMoistureThreshold` (0.6) |
| Forest | `height` 0.4–0.7, `moisture > 0.6` |
| Mountain | `height > MountainHeightThreshold` (default 0.7) |

### Height Compositing
- `GetBaseHeight()` — global noise only (`Clamp(TerrainNoise + RnrNoise, -1, 1) × Height`) — used for biome decision
- `GetHeightmap()` — base + biome HeightmapNoise offset + erosion compositing + detail noise

### Vertex Color Encoding
Biome is baked into mesh vertex colors during chunk generation:
- `R = primaryBiome / 10` (0=Ocean, 1=Grassland, 2=Forest, 3=Mountain)
- `G = blendFactor` (0–1 transition between primary and secondary)
- `B = secondaryBiome / 10`
- Shader decodes these to pick material colors/textures

## Common Tasks

### Adding a new biome
1. Add to `BiomeType` enum in TerrainController.cs
2. Update `GetBiome()` with new conditions
3. Create a new `BiomeGenerator` subclass in `scripts/Biomes/<Name>/`
4. Update `GetBiomeGenerator()` mapping in TerrainController.cs
5. Update GrassPlacer/TreePlacer density logic if needed
6. Add shader case in `terrain.gdshader`

### Modifying terrain generation
1. Edit `GetBaseHeight()` or `GetHeightmap()` in TerrainController.cs
2. Or adjust per-biome generators' HeightmapNoise/DetailWeight/ErosionWeight exports

### Adding vegetation density
1. Export new density property in GrassPlacer/TreePlacer
2. Update `GetBiomeDensity()` to use the new property
