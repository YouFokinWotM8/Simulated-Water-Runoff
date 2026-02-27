using System;

namespace RunoffMod
{
    // IMPORTANT: This name must match what your other code references.
    // Your build errors were because this was RunoffSettings while code referenced RunOffSettings.
    public class RunOffSettings
    {
        // === Tunables (defaults match your current behavior file) ===
        public float MaxDistance { get; set; } = 32f;

        public int TickGateModulo { get; set; } = 7;
        public double FaceSpawnChance { get; set; } = 0.70;
        public int MaxSpawnsPerBlockTick { get; set; } = 3;

        public float ChanceTopEdge { get; set; } = 0.55f;
        public float WallMinY { get; set; } = 0.15f;
        public float WallMaxY { get; set; } = 0.95f;

        public float DropMinSize { get; set; } = 0.18f;
        public float DropMaxSize { get; set; } = 0.22f;

        // “Face-plane spawn” distance (smaller = closer to wall)
        // You were using FaceEpsilon in code; this is now the GUI-tweakable version.
        public double FaceClearance { get; set; } = 0.00001;

        // Extra outward push for top-edge drips (to clear block beneath)
        public double TopEdgeExtraOut { get; set; } = 0.08;

        public int TrailSegments { get; set; } = 12;
        public double TrailLength { get; set; } = 0.85;
        public float TrailMinSize { get; set; } = 0.12f;
        public float TrailMaxSize { get; set; } = 0.18f;
        public double TrailSideJitter { get; set; } = 0.03;
        public double WallSurfaceProbeIn { get; set; } = 0.22;
    }
}
