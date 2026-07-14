using Godot;

namespace AdventureGame.Scripts;

[Tool]
public partial class GrassScatter : MultiMeshInstance3D
{
    [Export] public int InstanceCount { get; set; } = 2000;
    [Export] public float AreaWidth { get; set; } = 90.0f;
    [Export] public float AreaDepth { get; set; } = 90.0f;
    [Export] public float MinScale { get; set; } = 0.7f;
    [Export] public float MaxScale { get; set; } = 1.3f;

    public override void _Ready()
    {
        Populate();
    }

    private void Populate()
    {
        var mm = Multimesh;
        if (mm == null || !IsInstanceValid(mm))
            return;

        mm.InstanceCount = InstanceCount;
        var rng = new System.Random();

        for (int i = 0; i < InstanceCount; i++)
        {
            var x = (float)(rng.NextDouble() * AreaWidth - AreaWidth / 2.0);
            var z = (float)(rng.NextDouble() * AreaDepth - AreaDepth / 2.0);
            var rot = (float)(rng.NextDouble() * Mathf.Pi * 2.0);
            var scale = (float)(rng.NextDouble() * (MaxScale - MinScale) + MinScale);

            var t = Transform3D.Identity
                .Rotated(Vector3.Up, rot)
                .Scaled(new Vector3(scale, scale, scale))
                .Translated(new Vector3(x, 0, z));

            mm.SetInstanceTransform(i, t);
        }
    }
}
