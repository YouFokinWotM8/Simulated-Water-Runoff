using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RunoffMod
{
    public class BlockBehaviorRunoff : BlockBehavior
    {
        private static SimpleParticleProperties waterDrop;
        private static SimpleParticleProperties waterSplash;

        static BlockBehaviorRunoff()
        {
            // --- 1. The Falling Drop ---
            // CHANGED: Vanilla Rain Color (Pale Blue/White)
            // R=220, G=230, B=255 (Very light blue, almost white)
            // A=220 (High Alpha for strong visibility against dark blocks)
            int waterColor = ColorUtil.ToRgba(220, 230, 255, 220);

            waterDrop = new SimpleParticleProperties(
                1, 0,
                waterColor,
                new Vec3d(), new Vec3d(),
                new Vec3f(), new Vec3f(),
                1.0f, // Life
                0.8f, // Gravity
                0.15f, 0.15f, // Size (Small, as requested)
                EnumParticleModel.Cube
            );

            waterDrop.WindAffected = true;
            waterDrop.WithTerrainCollision = true;
            waterDrop.SelfPropelled = true;
            waterDrop.Async = true;

            // No OpacityEvolve: Keep it solid (220 Alpha) the entire way down.


            // --- 2. The Impact Splash ---
            // Matches the new vanilla rain color (Slightly more transparent)
            int splashColor = ColorUtil.ToRgba(220, 230, 255, 150);

            waterSplash = new SimpleParticleProperties(
                6, 6,
                splashColor,
                new Vec3d(), new Vec3d(),
                new Vec3f(-0.8f, 0.5f, -0.8f),
                new Vec3f(0.8f, 1.5f, 0.8f),
                0.08f,
                1.5f,
                0.15f, 0.3f,
                EnumParticleModel.Cube
            );
            waterSplash.Async = true;
        }

        public BlockBehaviorRunoff(Block block) : base(block)
        {
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
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
            if (RunoffModSystem.GlobalRainLevel < 0.05f) return;

            // Frequency: Every 3rd tick
            int tickHash = GameMath.oaatHash(pos.X, pos.Y, pos.Z + (int)(secondsTicking * 30));
            if (tickHash % 3 != 0) return;

            if (manager.BlockAccess.GetRainMapHeightAt(pos.X, pos.Z) > pos.Y) return;

            IBlockAccessor blockAccess = manager.BlockAccess;

            foreach (BlockFacing face in BlockFacing.HORIZONTALS)
            {
                BlockPos neighborPos = pos.AddCopy(face);
                Block neighbor = blockAccess.GetBlock(neighborPos);

                if (neighbor.Id == 0)
                {
                    BlockPos neighborDown = neighborPos.DownCopy();
                    Block neighborDownBlock = blockAccess.GetBlock(neighborDown);

                    if (!neighborDownBlock.SideSolid[BlockFacing.UP.Index])
                    {
                        SpawnDropletAndSplash(manager, blockAccess, pos, face, tickHash + face.Index * 100);
                    }
                }
            }
        }

        private void SpawnDropletAndSplash(IAsyncParticleManager manager, IBlockAccessor blockAccess, BlockPos pos, BlockFacing face, int seed)
        {
            int r1 = GameMath.oaatHash(seed, 0, 0);
            int r2 = GameMath.oaatHash(seed, 1, 0);
            float randHorizontal = (r1 & 0xFFFF) / 65536f;
            float randVertical = (r2 & 0xFFFF) / 65536f;

            double x = pos.X + 0.5 + (face.Normalf.X * 0.55);
            double y = pos.Y + 0.1 + (randVertical * 0.8);
            double z = pos.Z + 0.5 + (face.Normalf.Z * 0.55);

            double spread = 0.98;
            double offset = (spread * randHorizontal) - (spread / 2.0);
            if (face.IsAxisNS) x += offset;
            else z += offset;

            waterDrop.MinPos.Set(x, y, z);
            waterDrop.MinVelocity.Set(face.Normalf.X * 0.05f, -0.1f, face.Normalf.Z * 0.05f);

            manager.Spawn(waterDrop);

            // Splash Logic
            BlockPos checkPos = new BlockPos((int)x, (int)y, (int)z);

            for (int i = 1; i <= 12; i++)
            {
                checkPos.Y--;
                Block impactBlock = blockAccess.GetBlock(checkPos);

                if (impactBlock.SideSolid[BlockFacing.UP.Index] || impactBlock.IsLiquid())
                {
                    double impactY = checkPos.Y + 1.0;
                    if (impactBlock.IsLiquid()) impactY -= 0.2;

                    waterSplash.MinPos.Set(x, impactY, z);
                    manager.Spawn(waterSplash);
                    break;
                }
            }
        }
    }
}