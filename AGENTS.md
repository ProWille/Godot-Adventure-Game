# AGENTS.md - Adventure Game Development Guide

## Project Overview

Godot 4.x C# adventure game with chunk-based procedural terrain generation using FastNoiseLite for biome generation.

## Project Structure

```
/home/william/Development/Godot/Adventure-Game/
├── scripts/              # C# source files
│   ├── TerrainController.cs   # Main terrain/chunk system
│   ├── GrassController.cs     # Grass vegetation system
│   ├── TreeController.cs      # Tree vegetation system
│   ├── PlayerController.cs    # Player movement
│   └── CameraController.cs   # Camera controls
├── scenes/              # Godot scene files (.tscn)
│   ├── main.tscn
│   ├── player.tscn
│   └── test.tscn
├── shaders/             # GLSL shaders
│   └── terrain.gdshader
├── project.godot        # Godot project file
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
# Launch Godot editor (requires Godot 4.x installed)
godot --path .
# Or open via Godot editor GUI
```

### Run the game
```bash
# From Godot editor: F5 to run
# Or from command line (after export)
godot --path . --quit
```

### Code Analysis
```bash
# Check for errors (requires .NET SDK)
dotnet build --no-restore

# LSP errors shown in Godot editor automatically
```

### Testing
- **No test framework detected** - Consider adding one ( NUnit, xUnit, or Godot's built-in testing)
- To run a single test when tests exist:
```bash
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"
```

## Code Style Guidelines

### General Principles
- **File-scoped namespaces** - Use `namespace X.Y;` without braces
- **Partial classes** - Godot scripts use `public partial class`
- **Tool attribute** - Use `[Tool]` for editor-only scripts that run in the editor

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Private fields | `_camelCase` | `_speed`, `_chunkSize` |
| Public properties | PascalCase | `Speed`, `ChunkSize` |
| Methods | PascalCase | `GetHeight()`, `GenerateChunkMesh()` |
| Classes | PascalCase | `TerrainController`, `GrassController` |
| Enums | PascalCase | `BiomeType.Ocean` |
| Constants | PascalCase | `MaxHeight`, `DefaultChunkSize` |
| Files | PascalCase | `TerrainController.cs` |

### Export Pattern
Always use this pattern for Godot exports:

```csharp
[Export] private float _speed = 15.0f;
public float Speed => _speed;

// With property hint for editor sliders:
[Export(PropertyHint.Range, "1, 100, 1, prefer_slider")]
private int _resolution = 32;
public int Resolution
{
    get => _resolution;
    set
    {
        _resolution = Math.Clamp(value, 1, 100);
        RegenerateAllChunks();  // Trigger update on change
    }
}
```

### Import Order
```csharp
using System;
using System.Collections.Generic;
using System.Linq;  // If used
using Godot;
```

### Types and Initialization

- **Use explicit types** for Godot types: `float`, `int`, `Vector3`, `Color`, etc.
- **Prefer `new()` for collections**:
```csharp
private readonly Dictionary<Vector2I, MeshInstance3D> _chunks = [];
private readonly List<Vector3> _positions = new();
```
- **Default initialization for exports**: `[Export] private FastNoiseLite _noise = new();`
- **Use `IsInstanceValid()`** to check for null/invalid Godot objects

### Collections

| Type | Use Case |
|------|----------|
| `[]` / `Array` | Fixed-size arrays |
| `List<T>` | Dynamic lists |
| `Dictionary<K,V>` | Key-value maps with `[]` access |
| `HashSet<T>` | Unique items, O(1) lookup |
| `Queue<T>` | FIFO operations |

### Error Handling

- **Logging**: Use `GD.Print()` for info, `GD.PrintErr()` for errors
- **Null checks**: Use `IsInstanceValid(obj)` for Godot objects
- **Try pattern** for dictionaries:
```csharp
if (_chunks.TryGetValue(coord, out var chunk))
{
    chunk.QueueFree();
}
```

### Threading

- Use `System.Threading` for background work
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
- **Height noise**: Terrain elevation
- **Moisture noise**: Water/humidity levels (0-1 normalized)
- **Temperature noise**: Temperature levels (0-1 normalized)

Biomes are determined by moisture, temperature, and normalized height:
- Ocean, Grassland, Forest, Jungle, Desert, Savanna, Tundra, Mountain

Vertex colors store biome weights for shader-based texture blending.

## Common Tasks

### Adding a new biome
1. Add to `BiomeType` enum in TerrainController.cs
2. Update `GetBiome()` method with new conditions
3. Update `GetBiomeColorWeights()` for vertex colors
4. Update GrassController/TreeController density logic if needed

### Modifying terrain generation
1. Edit `GenerateChunkMeshData()` in TerrainController.cs
2. Changes auto-propagate due to property setters calling `RegenerateAllChunks()`

### Adding vegetation density
1. Export new density property in GrassController/TreeController
2. Update `GetBiomeDensity()` to use new property
