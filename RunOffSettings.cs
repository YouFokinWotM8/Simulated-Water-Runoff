using Vintagestory.API.Common;

namespace RunoffMod
{
    // Stored in ModConfig/runoffsettings.json (JSON).
    // ConfigLib writes to this file via configlib-patches.json, which lets players tune
    // these values in-game without you recompiling the DLL.
    public class RunOffSettings
    {
        // ==== Spawn frequency / density ====
        public int TickGateModulo = 10;
        public int MaxSpawnsPerBlockTick = 2;

        // Split tuning:
        // - FaceSpawnChance: chance to spawn a wall-face drip attempt per eligible face
        // - EdgeSpawnChance: chance to spawn a top-edge drip attempt per eligible face
        public float FaceSpawnChance = 0.55f;

        // New (replaces ChanceTopEdge as "probability of top-edge").
        public float EdgeSpawnChance = 0.55f;

        // Backwards-compatible alias (older configs might still write this field)
        // If present, it will just overwrite EdgeSpawnChance when the json is deserialized.
        public float ChanceTopEdge
        {
            get => EdgeSpawnChance;
            set => EdgeSpawnChance = value;
        }

        // Distance cull
        public float MaxDistance = 32f;

        // Wall spawn band (0..1 block height)
        public float WallMinY = 0.15f;
        public float WallMaxY = 0.95f;

        // ==== Drip visuals / placement ====
        // Cube size for drips
        public float DropSize = 0.30f;

        // How far outside the face plane the drip center is placed.
        // Smaller = closer to wall. (0 = exactly on the face plane)
        public float FaceClearance = 0.0005f;

        // Extra outward push for top-edge drips to avoid hitting the block below.
        public float TopEdgeExtraOut = 0.02f;

        // ==== Colors ====
        public int ColorR = 106;
        public int ColorG = 195;
        public int ColorB = 207;

        public int DropAlpha = 255;
        public int SplashAlpha = 235;
        public int TrailAlpha = 235;

        // ==== Physics / timing ====
        public float GravityConst = 9.81f;
        public float LifePaddingSeconds = 0.01f;

        // ==== Wall streaks ====
        public int TrailSegments = 10;
        public float TrailLength = 0.85f;
        public float TrailSideJitter = 0.03f;
        public float WallSurfaceProbeIn = 0.22f;

        public float TrailMinSize = 0.12f;
        public float TrailMaxSize = 0.18f;

        // ==== Splash ====
        public int SplashMinQty = 3;
        public int SplashAddQty = 3;
        public float SplashMinSize = 0.25f;
        public float SplashMaxSize = 0.40f;
    }
}