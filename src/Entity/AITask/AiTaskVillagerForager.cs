using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Picks whatever the claim will give: berries, mushrooms, reeds, fallen sticks.
    ///
    /// This is the job that stops a badly sited village being dead on arrival. Everything
    /// else in the mod needs something the ground has to already have. A settlement
    /// founded on bare rock has no woodlot to fell, no field worth tilling and no clay to
    /// dig, and without this it would simply starve while its lumberjack stood about
    /// waiting for a forest. A forager finds something almost anywhere, slowly.
    ///
    /// Slowly is the design. The yields here are low on purpose: foraging keeps a village
    /// breathing while it gets its real economy going, and a village that could live off
    /// berries forever would never need to farm.
    ///
    /// It works the claim rather than a plot, because the whole claim is the foraging
    /// ground and fencing off a berry patch would be absurd.
    /// </summary>
    public class AiTaskVillagerForager : AiTaskVillagerWork
    {
        protected override EnumTrade Trade => EnumTrade.Forager;

        /// <summary>Never used: a forager has no plot. Declared because the base asks.</summary>
        protected override EnumPlotKind PlotKind => EnumPlotKind.Terrace;

        protected override bool NeedsPlot => false;

        protected override int VerticalSearch => 2;

        /// <summary>
        /// Picking a berry is quicker than felling a tree, and there is a lot of walking
        /// between one bush and the next.
        /// </summary>
        protected override float WorkTimeSec => base.WorkTimeSec * 0.6f;

        public AiTaskVillagerForager(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        protected override bool IsTarget(Block block, BlockPos pos)
        {
            if (block == null || block.Id == 0) return false;

            string path = block.Code?.Path;
            if (path == null) return false;

            // Ripe berries. An unripe bush is left standing, which is the difference
            // between foraging and stripping the countryside.
            if (block is BlockBerryBush)
            {
                return path.EndsWith("-ripe");
            }

            // Mushrooms, reeds and the small ground plants worth bending down for. Worth
            // is not a judgement made here: the resource table already knows which
            // mushrooms a village will eat, so asking it keeps the forager away from the
            // deathcaps without a second list to maintain.
            bool candidate = path.StartsWith("mushroom-")
                          || path.StartsWith("tallgrass")
                          || path.StartsWith("smallberrybush")
                          || ((block is BlockPlant || block is BlockSeaweed)
                              && (path.Contains("flax") || path.Contains("reed") || path.Contains("cattail")));

            return candidate && WorthTaking(block, pos);
        }

        protected override bool Work(BlockPos pos)
        {
            Block block = entity.World.BlockAccessor.GetBlock(pos);

            // A berry bush is picked, not pulled up. Breaking it would give one harvest
            // and then nothing forever, which is exactly what a renewable bootstrap must
            // not do. The game already knows how to pick one: it is what a player's right
            // click does, and it leaves the bush behind at its empty stage.
            if (block is BlockBerryBush)
            {
                return Pick(block, pos);
            }

            return BreakAndCarry(pos) > 0;
        }

        /// <summary>
        /// Takes the fruit and leaves the bush.
        ///
        /// Done by hand rather than through the block's interact handler because that
        /// wants a player, and a villager is not one. Taking the drops and swapping the
        /// bush to its empty variant is the same outcome by the same rules.
        /// </summary>
        private bool Pick(Block bush, BlockPos pos)
        {
            ItemStack[] drops = bush.GetDrops(entity.World, pos, null);
            int taken = 0;

            if (drops != null)
            {
                foreach (ItemStack drop in drops)
                {
                    if (drop?.Collectible is Block) continue;   // the bush itself, not its fruit
                    taken += Harvest(drop);
                }
            }

            if (taken == 0) return false;

            Block empty = entity.World.GetBlock(bush.CodeWithVariant("state", "empty"));
            if (empty != null)
            {
                entity.World.BlockAccessor.SetBlock(empty.BlockId, pos);
                entity.World.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
            }

            return true;
        }
    }
}
