using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace RunoffMod
{
    public class BlockBehaviorRunoff : BlockBehavior
    {
        // Templates are rebuilt from settings so changes in ConfigLib GUI take effect.
        private static SimpleParticleProperties waterDropTemplate;
        private static SimpleParticleProperties waterSplashTemplate;
        private static SimpleParticleProperties wallTrailTemplate;

        private static readonly Random rand = new Random();
        private ICoreClientAPI capi;

        // Rebuild guard
        private static int lastTemplateHash = 0;
        private static bool forceRebuild = true;

        // Call from ModSystem when settings reload
        public static void ForceRebuildTemplates()
        {
            forceRebuild = true;
        }

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
        }

        public override void OnAsyncClientParticleTick(IAsyncParticleManager manager, BlockPos pos, float windAffectednessAtPos, float secondsTicking)
        {
            if (capi == null) return;

            var s = RunoffModSystem.Settings;
            if (s == null) return;

            EnsureTemplatesBuilt(s);

            // Rain gate
            if (RunoffModSystem.GlobalRainLevel < 0.05f) return;

            // Frequency gate
            int tickHash = GameMath.oaatHash(pos.X, pos.Y, pos.Z + (int)(secondsTicking * 30));
            if (tickHash % Math.Max(1, s.TickGateModulo) != 0) return;

            IBlockAccessor blockAccess = manager.BlockAccess;

            // Distance cull
            var player = capi.World?.Player;
            if (player?.Entity == null) return;
            float maxDist = Math.Max(1f, s.MaxDistance);
            if (player.Entity.Pos.SquareDistanceTo(pos.X, pos.Y, pos.Z) > (maxDist * maxDist)) return;

            // Outdoor gating
            if (blockAccess.GetRainMapHeightAt(pos.X, pos.Z) > pos.Y + 1) return;
            if (blockAccess.GetLightLevel(pos, EnumLightLevelType.OnlySunLight) <= 0) return;

            int spawnedThisTick = 0;
            int maxSpawns = Math.Max(1, s.MaxSpawnsPerBlockTick);

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

                // ================================
                // Split tuning: face drips vs edge drips
                // ================================
                // 1) Face drip attempt
                if (rand.NextDouble() <= GameMath.Clamp(s.FaceSpawnChance, 0f, 1f))
                {
                    SpawnDripSynced(manager, blockAccess, pos, face, topEdge: false);
                    spawnedThisTick++;
                    if (spawnedThisTick >= maxSpawns) break;
                }

                // 2) Edge drip attempt
                if (rand.NextDouble() <= GameMath.Clamp(s.EdgeSpawnChance, 0f, 1f))
                {
                    SpawnDripSynced(manager, blockAccess, pos, face, topEdge: true);
                    spawnedThisTick++;
                    if (spawnedThisTick >= maxSpawns) break;
                }
            }
        }

        private static void EnsureTemplatesBuilt(RunOffSettings s)
        {
            // Hash only the values that affect particle templates.
            unchecked
            {
                int h = 17;
                h = h * 31 + s.ColorR;
                h = h * 31 + s.ColorG;
                h = h * 31 + s.ColorB;
                h = h * 31 + s.DropAlpha;
                h = h * 31 + s.SplashAlpha;
                h = h * 31 + s.TrailAlpha;

                h = h * 31 + s.SplashMinQty;
                h = h * 31 + s.SplashAddQty;

                h = h * 31 + s.TrailSegments;
                h = h * 31 + FloatHash(s.DropSize);
                h = h * 31 + FloatHash(s.TrailMinSize);
                h = h * 31 + FloatHash(s.TrailMaxSize);
                h = h * 31 + FloatHash(s.SplashMinSize);
                h = h * 31 + FloatHash(s.SplashMaxSize);

                if (!forceRebuild && h == lastTemplateHash) return;

                forceRebuild = false;
                lastTemplateHash = h;
            }

            int dropColor = ColorUtil.ToRgba(GameMath.Clamp(s.DropAlpha, 0, 255), GameMath.Clamp(s.ColorR, 0, 255), GameMath.Clamp(s.ColorG, 0, 255), GameMath.Clamp(s.ColorB, 0, 255));
            int splashColor = ColorUtil.ToRgba(GameMath.Clamp(s.SplashAlpha, 0, 255), GameMath.Clamp(s.ColorR, 0, 255), GameMath.Clamp(s.ColorG, 0, 255), GameMath.Clamp(s.ColorB, 0, 255));
            int trailColor = ColorUtil.ToRgba(GameMath.Clamp(s.TrailAlpha, 0, 255), GameMath.Clamp(s.ColorR, 0, 255), GameMath.Clamp(s.ColorG, 0, 255), GameMath.Clamp(s.ColorB, 0, 255));

            float dropSize = Math.Max(0.01f, s.DropSize);

            // --- Drop ---
            waterDropTemplate = new SimpleParticleProperties(
                1, 0,
                dropColor,
                new Vec3d(), new Vec3d(),
                new Vec3f(), new Vec3f(),
                1.5f,
                0.8f,
                dropSize, dropSize,
                EnumParticleModel.Cube
            )
            {
                WindAffected = true,
                WithTerrainCollision = true,
                SelfPropelled = true,
                Async = true
            };

            // --- Splash ---
            waterSplashTemplate = new SimpleParticleProperties(
                Math.Max(0, s.SplashMinQty), Math.Max(0, s.SplashAddQty),
                splashColor,
                new Vec3d(), new Vec3d(),
                new Vec3f(-0.5f, 0.5f, -0.5f),
                new Vec3f(0.5f, 0.8f, 0.5f),
                0.1f,
                1.5f,
                Math.Max(0.01f, s.SplashMinSize), Math.Max(0.01f, s.SplashMaxSize),
                EnumParticleModel.Cube
            )
            {
                Async = false
            };

            // --- Wall trail ---
            wallTrailTemplate = new SimpleParticleProperties(
                1, 0,
                trailColor,
                new Vec3d(), new Vec3d(),
                new Vec3f(), new Vec3f(),
                0.25f,
                0.15f,
                Math.Max(0.01f, s.TrailMinSize), Math.Max(0.01f, s.TrailMaxSize),
                EnumParticleModel.Cube
            )
            {
                WindAffected = true,
                WithTerrainCollision = false,
                SelfPropelled = true,
                Async = true
            };
        }

        private static int FloatHash(float f) => BitConverter.SingleToInt32Bits(f);

        private void SpawnDripSynced(IAsyncParticleManager manager, IBlockAccessor blockAccess, BlockPos pos, BlockFacing face, bool topEdge)
        {
            var s = RunoffModSystem.Settings;
            if (s == null) return;

            // ======= FACE-PLANE SPAWN (config-driven) =======
            double clearance = Math.Max(0.0, s.FaceClearance);

            double x = pos.X + 0.5;
            double z = pos.Z + 0.5;

            if (face == BlockFacing.EAST) x = pos.X + 1.0 + clearance;
            if (face == BlockFacing.WEST) x = pos.X - clearance;
            if (face == BlockFacing.SOUTH) z = pos.Z + 1.0 + clearance;
            if (face == BlockFacing.NORTH) z = pos.Z - clearance;

            // Extra push ONLY for top-edge drips
            if (topEdge)
            {
                double extra = s.TopEdgeExtraOut;
                x += face.Normalf.X * extra;
                z += face.Normalf.Z * extra;
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
                y = pos.Y + (GameMath.Clamp(s.WallMinY, 0f, 1f) + (GameMath.Clamp(s.WallMaxY, 0f, 1f) - GameMath.Clamp(s.WallMinY, 0f, 1f)) * t);
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
            )
            {
                WindAffected = waterDropTemplate.WindAffected,
                WithTerrainCollision = waterDropTemplate.WithTerrainCollision,
                SelfPropelled = waterDropTemplate.SelfPropelled,
                Async = waterDropTemplate.Async,
                Color = waterDropTemplate.Color
            };

            drop.MinPos.Set(x, y, z);

            // Slight outward nudge so it "falls off" the face
            float nudge = topEdge ? 0.02f : 0.01f;
            drop.MinVelocity.Set(face.Normalf.X * nudge, 0f, face.Normalf.Z * nudge);

            drop.LifeLength = fallSeconds + Math.Max(0f, s.LifePaddingSeconds);

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
                    )
                    {
                        Async = false,
                        Color = waterSplashTemplate.Color
                    };

                    splash.MinPos.Set(x, impactY, z);
                    capi.World.SpawnParticles(splash);

                }, delayMs);
            }, "runoff_schedule_splash");
        }

        private void SpawnWallTrailLine(IAsyncParticleManager manager, double x, double y, double z, BlockFacing face, float lifeSeconds)
        {
            var s = RunoffModSystem.Settings;
            if (s == null) return;

            int segments = Math.Max(0, s.TrailSegments);
            if (segments <= 0) return;

            for (int i = 0; i < segments; i++)
            {
                double t = (segments <= 1) ? 0 : (double)i / (segments - 1);
                double yy = y - (t * s.TrailLength);

                if (!IsValidWallSurfaceAt(x, yy, z, face)) break;

                double side = (rand.NextDouble() - 0.5) * (s.TrailSideJitter * 2.0);
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
                )
                {
                    WindAffected = wallTrailTemplate.WindAffected,
                    WithTerrainCollision = wallTrailTemplate.WithTerrainCollision,
                    SelfPropelled = wallTrailTemplate.SelfPropelled,
                    Async = wallTrailTemplate.Async,
                    LifeLength = lifeSeconds,
                    Color = wallTrailTemplate.Color
                };

                trail.MinPos.Set(xx, yy, zz);
                trail.MinVelocity.Set(face.Normalf.X * 0.004f, -0.035f, face.Normalf.Z * 0.004f);

                manager.Spawn(trail);
            }
        }

        private bool IsValidWallSurfaceAt(double x, double y, double z, BlockFacing face)
        {
            var s = RunoffModSystem.Settings;
            if (s == null) return false;

            // Probe slightly "into" the wall behind the particle
            double px = x - face.Normalf.X * s.WallSurfaceProbeIn;
            double pz = z - face.Normalf.Z * s.WallSurfaceProbeIn;

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
            var s = RunoffModSystem.Settings;

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

            float gravityConst = (s != null) ? Math.Max(0.01f, s.GravityConst) : 9.81f;
            float effectiveG = gravityConst * waterDropTemplate.GravityEffect;

            fallSeconds = (float)Math.Sqrt((2.0 * d) / effectiveG);
            delayMs = (int)(fallSeconds * 1000f);
            if (delayMs < 1) delayMs = 1;

            return true;
        }
    }
}