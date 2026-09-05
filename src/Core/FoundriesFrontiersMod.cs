using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Root mod system. Owns startup, class registration and logging.
    /// Everything else hangs off this.
    /// </summary>
    public class FoundriesFrontiersMod : ModSystem
    {
        public const string ModId = "foundriesfrontiers";
        public const string LogPrefix = "[F&F]";

        /// <summary>
        /// When on, every villager's floating tag is replaced with its live state.
        /// The cheapest possible debug overlay: no client code, no renderer, and it
        /// works over the network for free because nametags already sync.
        /// </summary>
        public bool DebugLabels;

        private ICoreAPI api;
        private ICoreServerAPI sapi;

        public void Log(string message, params object[] args)
        {
            api?.Logger?.Notification(LogPrefix + " " + (args.Length == 0 ? message : string.Format(message, args)));
        }

        public void Warn(string message, params object[] args)
        {
            api?.Logger?.Warning(LogPrefix + " " + (args.Length == 0 ? message : string.Format(message, args)));
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;

            // Before anything else - other systems read config during their own startup.
            FFConfig.Load(api);

            // Must be registered on both sides - the client builds the entity too.
            api.RegisterEntity("FFVillager", typeof(FFVillager));
            api.RegisterEntityBehaviorClass("ffchatter", typeof(EntityBehaviorVillagerChatter));

            // Blocks. Both sides again: the client needs the class to draw and describe it.
            api.RegisterBlockClass("BlockVillageCairn", typeof(BlockVillageCairn));
            api.RegisterBlockEntityClass("VillageCairn", typeof(BlockEntityVillageCairn));

            // Our AI tasks. Registered by code so entity JSON can reference them,
            // which keeps tuning in data rather than requiring a rebuild.
            Vintagestory.GameContent.AiTaskRegistry.Register<AiTaskVillagerLoiter>("ffloiter");
            Vintagestory.GameContent.AiTaskRegistry.Register<AiTaskVillagerGoto>("ffgoto");
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            base.StartServerSide(sapi);
            this.sapi = sapi;

            Log("Foundries & Frontiers {0} starting (game {1})",
                Mod.Info.Version, GameVersion.ShortGameVersion);

            DevCommands.Register(sapi, this);

            Log("Ready. /ff status, /ff spawn, /ff trades registered.");
        }
    }
}
