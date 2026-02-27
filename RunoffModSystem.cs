using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace RunoffMod
{
    public class RunoffModSystem : ModSystem
    {
        public static float GlobalRainLevel = 0f;

        // Loaded from ModConfig/runoffsettings.json (JSON).
        // ConfigLib updates that file when the player changes settings in the in-game GUI.
        public static RunOffSettings Settings { get; private set; } = new RunOffSettings();

        private ICoreClientAPI capi;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;

            LoadSettings(api);

            // Optional: allow reloading settings in-game after changing them in ConfigLib UI.
            // Usage: /runoff reload
            api.ChatCommands.Create("runoff")
                .WithDescription("RunoffMod commands")
                .BeginSubCommand("reload")
                    .WithDescription("Reload runoff settings from ModConfig/runoffsettings.json")
                    .HandleWith(_ =>
                    {
                        LoadSettings(api);
                        // Force particle templates rebuild on next tick.
                        BlockBehaviorRunoff.ForceRebuildTemplates();
                        return TextCommandResult.Success("Runoff settings reloaded.");
                    })
                .EndSubCommand();

            // Keep rain level updated
            api.Event.RegisterGameTickListener(OnClientTick, 200);
        }

        private void LoadSettings(ICoreClientAPI api)
        {
            try
            {
                // JSON config in ModConfig. (ConfigLib uses the same file via its "file" mode.)
                var loaded = api.LoadModConfig<RunOffSettings>("runoffsettings.json");

                if (loaded == null)
                {
                    Settings = new RunOffSettings();
                    api.StoreModConfig(Settings, "runoffsettings.json");
                }
                else
                {
                    Settings = loaded;
                }
            }
            catch (Exception)
            {
                // If something goes wrong (corrupt file etc.), don't crash the client.
                Settings = new RunOffSettings();
            }
        }

        private void OnClientTick(float dt)
        {
            if (capi?.World == null) return;

            var rain = capi.World.BlockAccessor.GetRainMapHeightAt((int)capi.World.Player.Entity.Pos.X, (int)capi.World.Player.Entity.Pos.Z);
            // The original mod used a GlobalRainLevel float; keep behavior the same.
            // Use the client-side weather system if present; otherwise approximate with rain map + precipitation.
            try
            {
                // Most accurate: precipitation at player position
                GlobalRainLevel = capi.World.Player.Entity.World.BlockAccessor.GetClimateAt(
                    capi.World.Player.Entity.Pos.AsBlockPos,
                    EnumGetClimateMode.NowValues
                )?.Rainfall ?? 0f;
            }
            catch
            {
                // Fallback: just clamp to 0/1 based on rain map height being above player (rough)
                GlobalRainLevel = 0.5f;
            }
        }
    }
}