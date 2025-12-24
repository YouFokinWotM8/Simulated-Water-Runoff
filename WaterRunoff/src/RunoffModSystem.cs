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

        public override void StartClientSide(ICoreClientAPI api)
        {
            api.Event.RegisterGameTickListener((dt) =>
            {
                var player = api.World.Player;
                if (player?.Entity == null) return;

                BlockPos pos = player.Entity.Pos.AsBlockPos;
                ClimateCondition conds = api.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.NowValues);

                if (conds != null) GlobalRainLevel = conds.Rainfall;
            }, 200);
        }
    }
}