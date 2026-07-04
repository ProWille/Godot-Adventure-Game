using Godot;

namespace AdventureGame.Scripts;

public partial class MinimapOverlay : CanvasLayer
{
    [Export] private int _minimapSize = 128;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.01f, prefer_slider")]
    private float _minimapTransparency = 1.0f;
    [Export] private float _updateInterval = 0.25f;
    [Export] private bool _startVisible = false;

    private TerrainController _terrain;
    private TextureRect _textureRect;
    private Image _image;
    private ImageTexture _texture;
    private Timer _updateTimer;
    private Vector2 _margin = new(10, 10);

    private static readonly Color[] BiomeColors =
    [
        new(0.7f, 0.5f, 0.3f),
        new(0.6f, 0.9f, 0.3f),
        new(0.2f, 0.7f, 0.1f),
        new(0.6f, 0.6f, 0.7f),
        new(0.95f, 0.95f, 1.0f),
        new(0.85f, 0.7f, 0.4f)
    ];

    private Panel CreateMinimap()
    {
        var panel = new Panel
        {
            Size = new Vector2(_minimapSize, _minimapSize),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        AddChild(panel);

        _textureRect = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Keep,
            Size = new Vector2(_minimapSize, _minimapSize),
            Position = Vector2.Zero
        };
        panel.AddChild(_textureRect);

        _image = Image.CreateEmpty(_minimapSize, _minimapSize, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
        _textureRect.Texture = _texture;

        return panel;
    }

    private void CreateTimer()
    {
        _updateTimer = new Timer
        {
            WaitTime = _updateInterval,
            OneShot = false
        };
        _updateTimer.Timeout += UpdateMinimap;
        AddChild(_updateTimer);
        _updateTimer.Start();
    }

    private void Reposition(Panel panel)
    {
        if (!IsInstanceValid(panel)) return;
        var viewportSize = GetViewport().GetVisibleRect().Size;
        panel.Position = new Vector2(
            viewportSize.X - _minimapSize - _margin.X,
            _margin.Y
        );
    }

    private void UpdateMinimap()
    {
        if (!IsInstanceValid(_terrain) || !Visible)
            return;

        var activePlayer = _terrain.ActivePlayer;
        var playerPos = IsInstanceValid(activePlayer) ? activePlayer.GlobalPosition : Vector3.Zero;

        var halfCoverage = _terrain.ChunkSize * _terrain.RenderDistance;
        var pixelSize = halfCoverage * 2 / _minimapSize;

        for (int y = 0; y < _minimapSize; y++)
        {
            for (int x = 0; x < _minimapSize; x++)
            {
                var worldX = playerPos.X - halfCoverage + x * pixelSize;
                var worldZ = playerPos.Z - halfCoverage + y * pixelSize;
                var color = SampleBiome(worldX, worldZ);
                _image.SetPixel(x, y, color);
            }
        }

        _texture.SetImage(_image);
    }

    public override void _Ready()
    {
        Visible = _startVisible;

        _terrain = GetTree().CurrentScene?.GetNodeOrNull<TerrainController>("TerrainController");
        if (!IsInstanceValid(_terrain))
        {
            GD.PushError("TerrainController node not found in current scene.");
            return;
        }

        var panel = CreateMinimap();
        Reposition(panel);
        UpdateMinimap();
        GetViewport().SizeChanged += () => Reposition(panel);
        CreateTimer();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.M)
                Visible = !Visible;
        }
    }

    private Color SampleBiome(float worldX, float worldZ)
    {
        var biome = _terrain.GetBiome(worldX, worldZ);
        var idx = (int)biome;
        return idx >= 0 && idx < BiomeColors.Length ? new(BiomeColors[idx], _minimapTransparency) : new(Colors.Magenta, _minimapTransparency);
    }
}
