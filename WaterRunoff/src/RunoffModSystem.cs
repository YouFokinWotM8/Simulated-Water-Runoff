using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RunoffMod
{
    public class RunoffModSystem : ModSystem
    {
        public static float GlobalRainLevel = 0f;

        // Config (written by ConfigLib GUI into ModConfig/runoffsettings.json)
        public static RunOffSettings Settings { get; private set; } = new RunOffSettings();
        public static int SettingsVersion { get; private set; } = 0;

        private const string SettingsFileName = "runoffsettings.json";

        public override void Start(ICoreAPI api)
        {
            api.RegisterBlockBehaviorClass("Runoff", typeof(BlockBehaviorRunoff));

            // Load once on startup (client/server safe)
            LoadSettings(api);
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);

            // Iterate ALL blocks to apply the behavior
            foreach (Block block in api.World.Blocks)
            {
                if (block == null || block.Code == null || block.Id == 0) continue;

                // --- EXCLUSIONS ---
                if (block.BlockMaterial == EnumBlockMaterial.Leaves) continue;
                if (block.BlockMaterial == EnumBlockMaterial.Plant) continue;
                if (block.BlockMaterial == EnumBlockMaterial.Liquid) continue;
                if (block.BlockMaterial == EnumBlockMaterial.Snow) continue;
                if (block.BlockMaterial == EnumBlockMaterial.Fire) continue;

                if (block.RenderPass == EnumChunkRenderPass.Meta) continue;

                if (block.CollisionBoxes == null || block.CollisionBoxes.Length == 0) continue;

                if (block.HasBehavior<BlockBehaviorRunoff>()) continue;

                // --- INJECT BEHAVIOR ---
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
            // Update Global Rain Level
            api.Event.RegisterGameTickListener((dt) =>
            {
                var player = api.World.Player;
                if (player?.Entity == null) return;

                BlockPos pos = player.Entity.Pos.AsBlockPos;
                ClimateCondition conds = api.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.NowValues);

                GlobalRainLevel = (conds != null) ? conds.Rainfall : 0f;
            }, 500);

            // Reload settings occasionally so GUI edits can take effect without rebuild.
            // (Worst case: you change settings in GUI and it applies within a few seconds.)
            api.Event.RegisterGameTickListener((dt) =>
            {
                LoadSettings(api);
            }, 2000);
        }

        private void LoadSettings(ICoreAPI api)
        {
            try
            {
                var loaded = api.LoadModConfig<RunOffSettings>(SettingsFileName);
                if (loaded == null)
                {
                    // Create default config if missing
                    loaded = new RunOffSettings();
                    api.StoreModConfig(loaded, SettingsFileName);
                }

                Settings = loaded;
                SettingsVersion++;
            }
            catch (Exception e)
            {
                api.Logger.Warning($"[RunoffMod] Failed to load {SettingsFileName}: {e}");
                // Keep existing Settings if load fails, but bump version so dependents can re-check if needed
                SettingsVersion++;
            }
        }
    }
}
