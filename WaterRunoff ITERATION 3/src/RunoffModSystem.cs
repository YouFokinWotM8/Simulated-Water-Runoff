using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RunoffMod
{
    public class RunoffModSystem : ModSystem
    {
        public static float GlobalRainLevel = 0f;

        public override void Start(ICoreAPI api)
        {
            api.RegisterBlockBehaviorClass("Runoff", typeof(BlockBehaviorRunoff));
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);

            foreach (Block block in api.World.Blocks)
            {
                if (block == null || block.Code == null || block.Id == 0) continue;

                if (block.BlockMaterial == EnumBlockMaterial.Leaves) continue;
                if (block.BlockMaterial == EnumBlockMaterial.Plant) continue;
                if (block.BlockMaterial == EnumBlockMaterial.Liquid) continue;
                if (block.BlockMaterial == EnumBlockMaterial.Snow) continue;
                if (block.BlockMaterial == EnumBlockMaterial.Fire) continue;

                if (block.RenderPass == EnumChunkRenderPass.Meta) continue;
                if (block.CollisionBoxes == null || block.CollisionBoxes.Length == 0) continue;
                if (block.HasBehavior<BlockBehaviorRunoff>()) continue;

                BlockBehaviorRunoff behavior = new BlockBehaviorRunoff(block);

                if (block.BlockBehaviors == null)
                {
                    block.BlockBehaviors = new BlockBehavior[] { behavior };
                }
                else
                {
                    block.BlockBehaviors = block.BlockBehaviors.Append(behavior).ToArray();
                }

                behavior.OnLoaded(api);
            }
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            api.Event.RegisterGameTickListener((dt) =>
            {
                var player = api.World.Player;
                if (player?.Entity == null) return;

                BlockPos pos = player.Entity.Pos.AsBlockPos;
                ClimateCondition conds = api.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.NowValues);

                GlobalRainLevel = (conds != null) ? conds.Rainfall : 0f;
            }, 500);
        }
    }
}
