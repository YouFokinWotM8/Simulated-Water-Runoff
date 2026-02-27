using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace RunoffMod
{
    public class BlockBehaviorRunoff : BlockBehavior
    {
        private static SimpleParticleProperties waterDropTemplate;
        private static SimpleParticleProperties waterSplashTemplate;
        private static SimpleParticleProperties wallTrailTemplate;

        private static readonly object templateLock = new object();
        private static int templatesBuiltForSettingsVersion = -1;

        private static Random rand = new Random();
        private ICoreClientAPI capi;

        // === Color (your tuned blue-green) ===
        private const int LeafR = 106;
        private const int LeafG = 195;
        private const int LeafB = 207;

        private const int DropAlpha = 255;
        private const int SplashAlpha = 235;
        private const int TrailAlpha = 235;

        private static int DropColor => ColorUtil.ToRgba(DropAlpha, LeafR, LeafG, LeafB);
        private static int SplashColor => ColorUtil.ToRgba(SplashAlpha, LeafR, LeafG, LeafB);
        private static int TrailColor => ColorUtil.ToRgba(TrailAlpha, LeafR, LeafG, LeafB);

        // Physics sync
        private const float GravityConst = 9.81f;
        private const float LifePaddingSeconds = 0.01f;

        public BlockBehaviorRunoff(Block block) : base(block) { }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            capi = api as ICoreClientAPI;

            // Ensure something exists so behavior is not stripped / optimized out on some blocks
            if (block.ParticleProperties == null || block.ParticleProperties.Length == 0)
            {
                var dummyParticle = new AdvancedParticleProperties();
                dummyParticle.Quantity = NatFloat.Zero;
                dummyParticle.LifeLength = NatFloat.Zero;
                dummyParticle.HsvaColor = new NatFloat[] { NatFloat.Zero, NatFloat.Zero, NatFloat.Zero, NatFloat.Zero };
                block.ParticleProperties = new AdvancedParticleProperties[] { dummyParticle };
            }

            EnsureTemplatesFromSettings();
        }

        public override void OnAsyncClientParticleTick(IAsyncParticleManager manager, BlockPos pos, float windAffectednessAtPos, float secondsTicking)
        {
            if (capi == null) return;

            EnsureTemplatesFromSettings();

            // Rain gate
            if (RunoffModSystem.GlobalRainLevel < 0.05f) return;

            var s = RunoffModSystem.Settings ?? new RunOffSettings();

            int tickGateModulo = s.TickGateModulo <= 0 ? 7 : s.TickGateModulo;
            double faceSpawnChance = (s.FaceSpawnChance <= 0 || s.FaceSpawnChance > 1.0) ? 0.70 : s.FaceSpawnChance;
            int maxSpawnsPerBlockTick = s.MaxSpawnsPerBlockTick <= 0 ? 3 : s.MaxSpawnsPerBlockTick;
            float maxDistance = s.MaxDistance <= 0 ? 32f : s.MaxDistance;

            // Frequency gate
            int tickHash = GameMath.oaatHash(pos.X, pos.Y, pos.Z + (int)(secondsTicking * 30));
            if (tickHash % tickGateModulo != 0) return;

            IBlockAccessor blockAccess = manager.BlockAccess;

            // Distance cull
            var player = capi.World?.Player;
            if (player?.Entity == null) return;
            if (player.Entity.Pos.SquareDistanceTo(pos.X, pos.Y, pos.Z) > (maxDistance * maxDistance)) return;

            // Outdoor gating
            if (blockAccess.GetRainMapHeightAt(pos.X, pos.Z) > pos.Y + 1) return;
            if (blockAccess.GetLightLevel(pos, EnumLightLevelType.OnlySunLight) <= 0) return;

            int spawnedThisTick = 0;

            // Iterate faces — allow up to MaxSpawnsPerBlockTick
            foreach (BlockFacing face in BlockFacing.HORIZONTALS)
            {
                BlockPos neighborPos = pos.AddCopy(face);
                Block neighbor = blockAccess.GetBlock(neighborPos);

                // Allow air or plants (so it can drip off the edge into open space)
                if (neighbor.Id != 0 && !neighbor.BlockMaterial.ToString().Contains("Plant")) continue;

                int rainMapY = blockAccess.GetRainMapHeightAt(neighborPos.X, neighborPos.Z);
                if (rainMapY > neighborPos.Y) continue;

                // Avoid dripping into a ledge directly beneath the neighbor face
                BlockPos neighborDownPos = neighborPos.DownCopy();
                Block neighborDown = blockAccess.GetBlock(neighborDownPos);
                if (neighborDown.SideSolid[BlockFacing.UP.Index]) continue;

                // Stair/partial face check: if SideSolid says no, require an edge on that face
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

                // Probability
                if (rand.NextDouble() > faceSpawnChance) continue;

                SpawnDripSynced(manager, blockAccess, pos, face);
                spawnedThisTick++;

                if (spawnedThisTick >= maxSpawnsPerBlockTick) break;
            }
        }

        private void SpawnDripSynced(IAsyncParticleManager manager, IBlockAccessor blockAccess, BlockPos pos, BlockFacing face)
        {
            var s = RunoffModSystem.Settings ?? new RunOffSettings();

            float chanceTopEdge = (s.ChanceTopEdge < 0 || s.ChanceTopEdge > 1f) ? 0.55f : s.ChanceTopEdge;
            float wallMinY = s.WallMinY;
            float wallMaxY = s.WallMaxY;
            if (wallMaxY < wallMinY) { float tmp = wallMinY; wallMinY = wallMaxY; wallMaxY = tmp; }

            bool topEdge = rand.NextDouble() < chanceTopEdge;

            // ======= FACE-PLANE SPAWN (MAX CLOSE) driven by FaceClearance =======
            double faceClearance = s.FaceClearance;
            if (faceClearance < 0) faceClearance = 0;

            double x = pos.X + 0.5;
            double z = pos.Z + 0.5;

            if (face == BlockFacing.EAST) x = pos.X + 1.0 + faceClearance;
            if (face == BlockFacing.WEST) x = pos.X - faceClearance;
            if (face == BlockFacing.SOUTH) z = pos.Z + 1.0 + faceClearance;
            if (face == BlockFacing.NORTH) z = pos.Z - faceClearance;

            // Extra push ONLY for top-edge drips to clear the block beneath
            double topEdgeExtraOut = s.TopEdgeExtraOut;
            if (topEdgeExtraOut < 0) topEdgeExtraOut = 0;

            if (topEdge)
            {
                x += face.Normalf.X * topEdgeExtraOut;
                z += face.Normalf.Z * topEdgeExtraOut;
            }

            double y;
            if (topEdge)
            {
                y = pos.Y + 0.98;
            }
            else
            {
                double t = rand.NextDouble();
                t = 0.25 + 0.75 * t;
                y = pos.Y + (wallMinY + (wallMaxY - wallMinY) * t);
            }

            // Jitter along the face edge line
            double jitter = (rand.NextDouble() * 0.8) - 0.4;
            if (face.IsAxisNS) x += jitter;
            else z += jitter;

            if (!TryComputeImpact(blockAccess, x, y, z, out double impactY, out float fallSeconds, out int delayMs))
            {
                return;
            }

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

            drop.Color = DropColor;
            drop.MinPos.Set(x, y, z);

            // Slight outward nudge so it "falls off" the face, but stays glued visually
            float nudge = topEdge ? 0.02f : 0.01f;
            drop.MinVelocity.Set(face.Normalf.X * nudge, 0f, face.Normalf.Z * nudge);

            drop.LifeLength = fallSeconds + LifePaddingSeconds;
            manager.Spawn(drop);

            // Only streak on wall-face drips
            if (!topEdge)
            {
                SpawnWallTrailLine(manager, x, y, z, face, drop.LifeLength);
            }

            // Schedule splash at impact time (main thread)
            capi.Event.EnqueueMainThreadTask(() =>
            {
                capi.Event.RegisterCallback(_ =>
                {
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
                    splash.Color = SplashColor;
                    splash.MinPos.Set(x, impactY, z);

                    capi.World.SpawnParticles(splash);

                }, delayMs);
            }, "runoff_schedule_splash");
        }

        private void SpawnWallTrailLine(IAsyncParticleManager manager, double x, double y, double z, BlockFacing face, float lifeSeconds)
        {
            var s = RunoffModSystem.Settings ?? new RunOffSettings();

            int trailSegments = s.TrailSegments <= 0 ? 12 : s.TrailSegments;
            double trailLength = s.TrailLength <= 0 ? 0.85 : s.TrailLength;
            double trailSideJitter = s.TrailSideJitter;
            if (trailSideJitter < 0) trailSideJitter = 0;

            for (int i = 0; i < trailSegments; i++)
            {
                double t = (trailSegments <= 1) ? 0 : (double)i / (trailSegments - 1);
                double yy = y - (t * trailLength);

                if (!IsValidWallSurfaceAt(x, yy, z, face)) break;

                double side = (rand.NextDouble() - 0.5) * (trailSideJitter * 2.0);
                double xx = x;
                double zz = z;
                if (face.IsAxisNS) xx += side;
                else zz += side;

                var trail = new SimpleParticleProperties(
                    wallTrailTemplate.MinQuantity, wallTrailTemplate.AddQuantity,
                    wallTrailTemplate.Color,
                    new Vec3d(), new Vec3d(),
                    new Vec3f(), new Vec3f(),
                    wallTrailTemplate.LifeLength,
                    wallTrailTemplate.GravityEffect,
                    wallTrailTemplate.MinSize, wallTrailTemplate.MaxSize,
                    wallTrailTemplate.ParticleModel
                );

                trail.WindAffected = wallTrailTemplate.WindAffected;
                trail.WithTerrainCollision = wallTrailTemplate.WithTerrainCollision;
                trail.SelfPropelled = wallTrailTemplate.SelfPropelled;
                trail.Async = wallTrailTemplate.Async;

                trail.LifeLength = lifeSeconds;
                trail.Color = TrailColor;

                trail.MinPos.Set(xx, yy, zz);
                trail.MinVelocity.Set(face.Normalf.X * 0.004f, -0.035f, face.Normalf.Z * 0.004f);

                manager.Spawn(trail);
            }
        }

        private bool IsValidWallSurfaceAt(double x, double y, double z, BlockFacing face)
        {
            var s = RunoffModSystem.Settings ?? new RunOffSettings();
            double probeIn = s.WallSurfaceProbeIn;
            if (probeIn < 0) probeIn = 0;

            // Probe slightly "into" the wall behind the particle
            double px = x - face.Normalf.X * probeIn;
            double pz = z - face.Normalf.Z * probeIn;

            BlockPos behindPos = new BlockPos((int)Math.Floor(px), (int)Math.Floor(y), (int)Math.Floor(pz));
            Block behind = capi.World.BlockAccessor.GetBlock(behindPos);
            if (behind == null || behind.Id == 0) return false;
            if (behind.BlockMaterial == EnumBlockMaterial.Plant) return false;
            if (behind.IsLiquid()) return false;

            // Ensure the space in front of the wall is open-ish
            BlockPos frontPos = behindPos.AddCopy(face);
            Block front = capi.World.BlockAccessor.GetBlock(frontPos);
            if (front != null && front.Id != 0 && front.BlockMaterial != EnumBlockMaterial.Plant) return false;

            // If face is solid, we're good
            if (behind.SideSolid[face.Index]) return true;

            // Otherwise, check collision boxes for an edge on that face
            Cuboidf[] boxes = behind.GetCollisionBoxes(capi.World.BlockAccessor, behindPos);
            if (boxes == null) return false;

            foreach (var box in boxes)
            {
                if (face == BlockFacing.NORTH && box.Z1 <= 0.05) return true;
                if (face == BlockFacing.SOUTH && box.Z2 >= 0.95) return true;
                if (face == BlockFacing.WEST && box.X1 <= 0.05) return true;
                if (face == BlockFacing.EAST && box.X2 >= 0.95) return true;
            }

            return false;
        }

        private bool TryComputeImpact(IBlockAccessor blockAccess, double x, double y, double z,
            out double impactY, out float fallSeconds, out int delayMs)
        {
            impactY = 0;
            fallSeconds = 0;
            delayMs = 0;

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

            float effectiveG = GravityConst * waterDropTemplate.GravityEffect;

            fallSeconds = (float)Math.Sqrt((2.0 * d) / effectiveG);
            delayMs = (int)(fallSeconds * 1000f);
            if (delayMs < 1) delayMs = 1;

            return true;
        }

        private static void EnsureTemplatesFromSettings()
        {
            int currentVersion = RunoffModSystem.SettingsVersion;
            if (currentVersion == templatesBuiltForSettingsVersion) return;

            lock (templateLock)
            {
                if (currentVersion == templatesBuiltForSettingsVersion) return;

                var s = RunoffModSystem.Settings ?? new RunOffSettings();

                float dropMin = s.DropMinSize <= 0 ? 0.18f : s.DropMinSize;
                float dropMax = s.DropMaxSize <= 0 ? 0.22f : s.DropMaxSize;

                float trailMin = s.TrailMinSize <= 0 ? 0.12f : s.TrailMinSize;
                float trailMax = s.TrailMaxSize <= 0 ? 0.18f : s.TrailMaxSize;

                // --- Drop ---
                waterDropTemplate = new SimpleParticleProperties(
                    1, 0,
                    DropColor,
                    new Vec3d(), new Vec3d(),
                    new Vec3f(), new Vec3f(),
                    1.5f,
                    0.8f,
                    dropMin, dropMax,
                    EnumParticleModel.Cube
                );

                waterDropTemplate.WindAffected = true;
                waterDropTemplate.WithTerrainCollision = true;
                waterDropTemplate.SelfPropelled = true;
                waterDropTemplate.Async = true;

                // --- Splash ---
                waterSplashTemplate = new SimpleParticleProperties(
                    3, 3,
                    SplashColor,
                    new Vec3d(), new Vec3d(),
                    new Vec3f(-0.5f, 0.5f, -0.5f),
                    new Vec3f(0.5f, 0.8f, 0.5f),
                    0.1f,
                    1.5f,
                    0.25f, 0.4f,
                    EnumParticleModel.Cube
                );

                waterSplashTemplate.Async = false;

                // --- Wall trail ---
                wallTrailTemplate = new SimpleParticleProperties(
                    1, 0,
                    TrailColor,
                    new Vec3d(), new Vec3d(),
                    new Vec3f(), new Vec3f(),
                    0.25f,
                    0.15f,
                    trailMin, trailMax,
                    EnumParticleModel.Cube
                );

                wallTrailTemplate.WindAffected = true;
                wallTrailTemplate.WithTerrainCollision = false;
                wallTrailTemplate.SelfPropelled = true;
                wallTrailTemplate.Async = true;

                templatesBuiltForSettingsVersion = currentVersion;
            }
        }
    }
}
