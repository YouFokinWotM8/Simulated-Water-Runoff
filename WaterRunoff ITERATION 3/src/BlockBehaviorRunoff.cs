using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace RunoffMod
{
    public class BlockBehaviorRunoff : BlockBehavior
    {
        // Base templates (NEVER mutated at runtime)
        private static SimpleParticleProperties waterDropTemplate;
        private static SimpleParticleProperties waterSplashTemplate;

        private static Random rand = new Random();

        private ICoreClientAPI capi;

        // === Tunables ===
        private const float MaxDistance = 32f;

        // Spawn behavior: you want BOTH top edge and wall-face runoff
        private const float ChanceTopEdge = 0.55f;   // % of the time spawn at top lip
        private const float WallMinY = 0.15f;
        private const float WallMaxY = 0.95f;

        // Push outward enough so cube (size 0.3) doesn't overlap the wall and get killed
        // half-size = 0.15. 0.5 puts center on face plane. Need at least 0.5 + 0.15 + clearance.
        private const double DropSize = 0.30;
        private const double DropHalfSize = DropSize / 2.0;
        private const double Clearance = 0.03;
        private const double SafeOutward = 0.5 + DropHalfSize + Clearance; // 0.68

        // Physics sync
        private const float GravityConst = 9.81f;     // baseline gravity
        private const float LifePaddingSeconds = 0.01f; // tiny cushion so it dies basically at impact

        static BlockBehaviorRunoff()
        {
            // --- 1. The Drop ---
            int waterColor = ColorUtil.ToRgba(200, 220, 230, 255);

            waterDropTemplate = new SimpleParticleProperties(
                1, 0,
                waterColor,
                new Vec3d(), new Vec3d(),
                new Vec3f(), new Vec3f(),
                1.5f,           // life (overridden per-spawn)
                0.8f,           // gravity effect
                0.3f, 0.3f,     // size
                EnumParticleModel.Cube
            );
            waterDropTemplate.WindAffected = true;
            waterDropTemplate.WithTerrainCollision = true;
            waterDropTemplate.SelfPropelled = true;
            waterDropTemplate.Async = true;

            // --- 2. The Splash ---
            int splashColor = ColorUtil.ToRgba(200, 220, 230, 150);
            waterSplashTemplate = new SimpleParticleProperties(
                3, 3,
                splashColor,
                new Vec3d(), new Vec3d(),
                new Vec3f(-0.5f, 0.5f, -0.5f),
                new Vec3f(0.5f, 0.8f, 0.5f),
                0.1f,
                1.5f,
                0.25f, 0.4f,
                EnumParticleModel.Cube
            );

            // We'll spawn splash on main thread, so it does NOT need Async.
            waterSplashTemplate.Async = false;
        }

        public BlockBehaviorRunoff(Block block) : base(block) { }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            capi = api as ICoreClientAPI;

            // Keep your "dummy particle" trick so async ticks fire for blocks
            if (block.ParticleProperties == null || block.ParticleProperties.Length == 0)
            {
                var dummyParticle = new AdvancedParticleProperties();
                dummyParticle.Quantity = NatFloat.Zero;
                dummyParticle.LifeLength = NatFloat.Zero;
                dummyParticle.HsvaColor = new NatFloat[] { NatFloat.Zero, NatFloat.Zero, NatFloat.Zero, NatFloat.Zero };
                block.ParticleProperties = new AdvancedParticleProperties[] { dummyParticle };
            }
        }

        public override void OnAsyncClientParticleTick(IAsyncParticleManager manager, BlockPos pos, float windAffectednessAtPos, float secondsTicking)
        {
            if (capi == null) return;

            // 1. Global Rain Check
            if (RunoffModSystem.GlobalRainLevel < 0.05f) return;

            // 2. Frequency (Optimized)
            int tickHash = GameMath.oaatHash(pos.X, pos.Y, pos.Z + (int)(secondsTicking * 30));
            if (tickHash % 15 != 0) return;

            IBlockAccessor blockAccess = manager.BlockAccess;

            // 2.5 Distance cull (big perf win)
            var player = capi.World?.Player;
            if (player?.Entity == null) return;
            if (player.Entity.Pos.SquareDistanceTo(pos.X, pos.Y, pos.Z) > (MaxDistance * MaxDistance)) return;

            // 3. SOURCE BLOCK EXPOSURE CHECK (keep your working gating)
            if (blockAccess.GetRainMapHeightAt(pos.X, pos.Z) > pos.Y + 1) return;
            if (blockAccess.GetLightLevel(pos, EnumLightLevelType.OnlySunLight) <= 0) return;

            // 4. CHECK ALL FACES
            foreach (BlockFacing face in BlockFacing.HORIZONTALS)
            {
                // -- A. Neighbor Validity --
                BlockPos neighborPos = pos.AddCopy(face);
                Block neighbor = blockAccess.GetBlock(neighborPos);

                // Must be Air (or plant)
                if (neighbor.Id != 0 && !neighbor.BlockMaterial.ToString().Contains("Plant")) continue;

                // -- B. FACE INDOOR CHECK (Prevent Indoor Leaks) --
                int rainMapY = blockAccess.GetRainMapHeightAt(neighborPos.X, neighborPos.Z);
                if (rainMapY > neighborPos.Y) continue;

                // -- C. STAIR/STEP CHECK (Prevent Clipping) --
                BlockPos neighborDownPos = neighborPos.DownCopy();
                Block neighborDown = blockAccess.GetBlock(neighborDownPos);
                if (neighborDown.SideSolid[BlockFacing.UP.Index]) continue;

                // -- D. Source Edge Check --
                if (!block.SideSolid[face.Index])
                {
                    Cuboidf[] boxes = block.GetCollisionBoxes(blockAccess, pos);
                    bool hasEdge = false;
                    if (boxes != null)
                    {
                        foreach (var box in boxes)
                        {
                            if (face == BlockFacing.NORTH && box.Z1 <= 0.05) hasEdge = true;
                            if (face == BlockFacing.SOUTH && box.Z2 >= 0.95) hasEdge = true;
                            if (face == BlockFacing.WEST && box.X1 <= 0.05) hasEdge = true;
                            if (face == BlockFacing.EAST && box.X2 >= 0.95) hasEdge = true;
                        }
                    }
                    if (!hasEdge) continue;
                }

                // -- E. Probability --
                if (rand.NextDouble() > 0.33) continue;

                // -- F. Spawn --
                SpawnDripSynced(manager, blockAccess, pos, face);
            }
        }

        private void SpawnDripSynced(IAsyncParticleManager manager, IBlockAccessor blockAccess, BlockPos pos, BlockFacing face)
        {
            // Decide whether this is a top-edge drip or a wall-face drip
            bool topEdge = rand.NextDouble() < ChanceTopEdge;

            // OUTWARD: safe offset for cube size 0.3 (prevents invisible insta-kill)
            double x = pos.X + 0.5 + (face.Normalf.X * SafeOutward);
            double z = pos.Z + 0.5 + (face.Normalf.Z * SafeOutward);

            // Y: top edge OR random wall height
            double y;
            if (topEdge)
            {
                y = pos.Y + 0.95; // top lip
            }
            else
            {
                // wall runoff: random height with slight bias upward
                double t = rand.NextDouble();
                t = 0.25 + 0.75 * t;
                y = pos.Y + (WallMinY + (WallMaxY - WallMinY) * t);
            }

            // Jitter sideways along the face
            double jitter = (rand.NextDouble() * 0.8) - 0.4;
            if (face.IsAxisNS) x += jitter;
            else z += jitter;

            // Find ground impact and compute fall time
            if (!TryComputeImpact(blockAccess, x, y, z, out double impactY, out float fallSeconds, out int delayMs))
            {
                return;
            }

            // --- Create per-spawn DROP (avoid mutating static template in async context) ---
            var drop = new SimpleParticleProperties(
                waterDropTemplate.MinQuantity, waterDropTemplate.AddQuantity,
                waterDropTemplate.Color,
                new Vec3d(), new Vec3d(),
                new Vec3f(), new Vec3f(),
                waterDropTemplate.LifeLength,
                waterDropTemplate.GravityEffect,
                waterDropTemplate.MinSize, waterDropTemplate.MaxSize,
                waterDropTemplate.ParticleModel
            );

            drop.WindAffected = waterDropTemplate.WindAffected;
            drop.WithTerrainCollision = waterDropTemplate.WithTerrainCollision;
            drop.SelfPropelled = waterDropTemplate.SelfPropelled;
            drop.Async = waterDropTemplate.Async;

            drop.MinPos.Set(x, y, z);
            drop.MinVelocity.Set(face.Normalf.X * 0.02f, 0f, face.Normalf.Z * 0.02f);

            // Life ends right at impact (plus tiny cushion)
            drop.LifeLength = fallSeconds + LifePaddingSeconds;

            manager.Spawn(drop);

            // --- Schedule splash at exact impact time on the MAIN THREAD ---
            // Important: RegisterCallback must be invoked from main thread in many builds.
            capi.Event.EnqueueMainThreadTask(() =>
            {
                capi.Event.RegisterCallback(_ =>
                {
                    // Create per-spawn SPLASH (no shared mutation)
                    var splash = new SimpleParticleProperties(
                        waterSplashTemplate.MinQuantity, waterSplashTemplate.AddQuantity,
                        waterSplashTemplate.Color,
                        new Vec3d(), new Vec3d(),
                        waterSplashTemplate.MinVelocity, waterSplashTemplate.AddVelocity,
                        waterSplashTemplate.LifeLength,
                        waterSplashTemplate.GravityEffect,
                        waterSplashTemplate.MinSize, waterSplashTemplate.MaxSize,
                        waterSplashTemplate.ParticleModel
                    );

                    splash.Async = false;
                    splash.MinPos.Set(x, impactY, z);

                    // Spawn on world (main thread safe)
                    capi.World.SpawnParticles(splash);

                }, delayMs);

            }, "runoff_schedule_splash");
        }

        private bool TryComputeImpact(IBlockAccessor blockAccess, double x, double y, double z,
            out double impactY, out float fallSeconds, out int delayMs)
        {
            impactY = 0;
            fallSeconds = 0;
            delayMs = 0;

            // Ray down up to 24 blocks (adjust if you want deeper)
            int startY = (int)Math.Floor(y);
            int minY = Math.Max(0, startY - 24);

            BlockPos checkPos = new BlockPos((int)Math.Floor(x), startY, (int)Math.Floor(z));

            for (int yy = startY; yy >= minY; yy--)
            {
                checkPos.Y = yy;
                Block hit = blockAccess.GetBlock(checkPos);
                if (hit == null || hit.Id == 0) continue;

                if (hit.IsLiquid())
                {
                    impactY = checkPos.Y + 0.9;
                    break;
                }

                if (hit.SideSolid[BlockFacing.UP.Index])
                {
                    impactY = checkPos.Y + 1.0;
                    break;
                }
            }

            if (impactY <= 0) return false;

            double d = y - impactY;
            if (d <= 0.05) return false;

            // Effective gravity matches particle's GravityEffect (0.8 in your template)
            float effectiveG = GravityConst * waterDropTemplate.GravityEffect;

            fallSeconds = (float)Math.Sqrt((2.0 * d) / effectiveG);
            delayMs = (int)(fallSeconds * 1000f);
            if (delayMs < 1) delayMs = 1;

            return true;
        }
    }
}
