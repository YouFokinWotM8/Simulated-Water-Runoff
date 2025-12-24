using System.Linq; // Required for .Append()
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
            // Register the behavior class so the game knows it exists
            api.RegisterBlockBehaviorClass("Runoff", typeof(BlockBehaviorRunoff));
        }

        // BRUTE FORCE INJECTION
        // This runs after all JSON assets are loaded. We iterate over every block in the game
        // and attach the behavior dynamically.
        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);

            foreach (Block block in api.World.Blocks)
            {
                if (block == null || block.Code == null) continue;

                // --- FILTERS ---
                // Skip Air
                if (block.Id == 0) continue;

                // Skip Liquids (Water, Lava, etc)
                if (block.IsLiquid()) continue;

                // Optional: Skip plants or things with no collision if you find they look weird
                // if (block.CollisionBoxes == null) continue; 

                // Prevent duplicates if you kept the JSON file
                if (block.HasBehavior<BlockBehaviorRunoff>()) continue;

                // --- INJECTION ---
                BlockBehaviorRunoff behavior = new BlockBehaviorRunoff(block);

                if (block.BlockBehaviors == null)
                {
                    block.BlockBehaviors = new BlockBehavior[] { behavior };
                }
                else
                {
                    block.BlockBehaviors = block.BlockBehaviors.Append(behavior).ToArray();
                }

                // Important: Manually trigger OnLoaded because the game missed it during JSON loading
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

                if (conds != null) GlobalRainLevel = conds.Rainfall;
            }, 200);
        }
    }
}