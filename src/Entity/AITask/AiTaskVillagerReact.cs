using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FoundriesFrontiers
{
    /// <summary>
    /// What a villager does about being hurt.
    ///
    /// One task rather than two, because fleeing and fighting are the same decision
    /// answered differently, and a villager has to be able to change their mind mid
    /// fight. A guard who is losing and has somebody to fall back behind should fall
    /// back, and that is not a different task, it is the same one reconsidering.
    ///
    /// Outranks everything except an explicit order. Being attacked is more urgent than
    /// bedtime, and a villager who keeps strolling while something bites them is the
    /// single most immersion breaking thing a village could do.
    /// </summary>
    public class AiTaskVillagerReact : FFTaskBase
    {
        private bool fighting;
        private double nextSwingAt;
        private double retreatUntil;
        private BlockPos fleeTarget;

        public AiTaskVillagerReact(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        protected override float ThinkIntervalSec => 0.4f;

        /// <summary>Always. Something attacking a village unwatched is still an attack.</summary>
        protected override float ObservedRangeBlocks => -1;

        protected override bool ShouldRun() => Villager != null && Villager.IsThreatened;

        protected override void OnStart()
        {
            fighting = ShouldFight();
            nextSwingAt = 0;
            retreatUntil = 0;
            fleeTarget = null;

            Villager.SaySomething(fighting
                ? EnumVillagerUtterance.Complain
                : EnumVillagerUtterance.Hurt);

            if (!fighting) StartRunning();
        }

        protected override bool OnTick(float dt)
        {
            if (!Villager.IsThreatened)
            {
                Villager.ForgetThreat();
                return false;
            }

            Entity threat = Villager.Threat;
            if (threat == null || !threat.Alive) return false;

            // Reconsider. A guard who is losing badly and has someone to fall back
            // behind should fall back, and a villager who was running but is cornered
            // has nothing left to lose.
            if (fighting && ShouldRetreat())
            {
                fighting = false;
                StartRunning();
            }

            return fighting ? Fight(threat) : Flee(threat);
        }

        // --- fighting ---------------------------------------------------------------

        private bool Fight(Entity threat)
        {
            double distSq = entity.Pos.SquareDistanceTo(threat.Pos.XYZ);
            var cfg = FFConfig.Current.Villager;

            if (distSq > cfg.AttackRangeBlocks * cfg.AttackRangeBlocks)
            {
                // Close the distance. Ordering it every tick would thrash the pathfinder,
                // so only when there is no order already in flight.
                if (Villager.GotoTarget == null) Villager.OrderGoto(threat.Pos.AsBlockPos, MoveSpeeds.Run);
                return true;
            }

            Villager.CancelGoto();
            entity.ServerControls.StopAllMovement();

            // Face what they are hitting.
            double dx = threat.Pos.X - entity.Pos.X;
            double dz = threat.Pos.Z - entity.Pos.Z;
            entity.Pos.Yaw = (float)Math.Atan2(dx, dz);

            double now = entity.World.ElapsedMilliseconds / 1000.0;
            if (now < nextSwingAt) return true;

            nextSwingAt = now + cfg.AttackIntervalSec;
            entity.AnimManager?.StartAnimation("hit");

            threat.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Entity,
                SourceEntity = entity,
                Type = EnumDamageType.BluntAttack,
                DamageTier = 0
            }, DamageDealt());

            return true;
        }

        /// <summary>Bare hands, unless they are carrying something with an edge.</summary>
        private float DamageDealt()
        {
            var cfg = FFConfig.Current.Villager;
            float withTool = Villager.ToolTier * cfg.DamagePerToolTier;
            return cfg.UnarmedDamage + withTool;
        }

        // --- running ----------------------------------------------------------------

        private bool Flee(Entity threat)
        {
            double now = entity.World.ElapsedMilliseconds / 1000.0;

            // Keep putting distance between them until the fright wears off.
            if (Villager.GotoTarget == null && now >= retreatUntil)
            {
                StartRunning();
            }

            return true;
        }

        private void StartRunning()
        {
            Entity threat = Villager.Threat;
            if (threat == null) return;

            var cfg = FFConfig.Current.Villager;
            retreatUntil = entity.World.ElapsedMilliseconds / 1000.0 + 2;

            // Away from the threat, and toward home if there is one, because a villager
            // who runs into the wilderness has swapped one death for another.
            //
            // Direction first, then a fixed distance along it. Adding the offset and then
            // averaging with the village centre used to collapse to a point a block or two
            // away whenever the villager was already near the middle of their own village,
            // and a one block journey is no journey at all: the pathfinder refuses it, the
            // walk instantly "arrives", and the villager twitches on the spot being killed.
            double dx = entity.Pos.X - threat.Pos.X;
            double dz = entity.Pos.Z - threat.Pos.Z;

            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 0.01) { dx = 1; dz = 0; len = 1; }
            dx /= len;
            dz /= len;

            Village village = Home;
            if (village != null)
            {
                // Bias the run toward home without letting it point back at the threat.
                double hx = village.CentreX - entity.Pos.X;
                double hz = village.CentreZ - entity.Pos.Z;
                double hlen = Math.Sqrt(hx * hx + hz * hz);
                if (hlen > 1)
                {
                    dx += hx / hlen * 0.6;
                    dz += hz / hlen * 0.6;

                    double mix = Math.Sqrt(dx * dx + dz * dz);
                    if (mix > 0.01) { dx /= mix; dz /= mix; }
                }
            }

            int runTo = FleeDistanceBlocks;
            int tx = (int)(entity.Pos.X + dx * runTo);
            int tz = (int)(entity.Pos.Z + dz * runTo);

            var probe = new BlockPos(tx, 0, tz, 0);
            int y = entity.World.BlockAccessor.GetTerrainMapheightAt(probe);
            if (y <= 0)
            {
                // Nowhere to run that way. Head for the village centre instead, which is
                // at least somewhere, rather than ordering no journey at all and standing
                // still until the threat memory runs out.
                if (village == null) return;
                fleeTarget = village.Centre;
            }
            else
            {
                fleeTarget = new BlockPos(tx, y + 1, tz, 0);
            }

            Villager.OrderGoto(fleeTarget, MoveSpeeds.Run);
        }

        // --- deciding ---------------------------------------------------------------

        /// <summary>How far a frightened villager runs before reconsidering.</summary>
        private static int FleeDistanceBlocks => 14;

        private bool ShouldFight() => Villager.Courage == EnumCourage.Bold;

        /// <summary>
        /// A guard fights to the death alone, because there is nobody to fall back
        /// behind. With another fighter nearby, badly hurt means retreat: the line holds
        /// either way and the village keeps the guard.
        /// </summary>
        private bool ShouldRetreat()
        {
            var cfg = FFConfig.Current.Villager;

            float health = Health();
            if (health > cfg.RetreatBelowHealthFraction) return false;

            return HasSupportNearby();
        }

        private float Health()
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("health");
            if (tree == null) return 1f;

            float max = tree.GetFloat("maxhealth", 1f);
            return max <= 0 ? 1f : tree.GetFloat("currenthealth", max) / max;
        }

        private bool HasSupportNearby()
        {
            float range = FFConfig.Current.Villager.SupportRangeBlocks;

            foreach (Entity other in entity.World.GetEntitiesAround(entity.Pos.XYZ, range, range,
                                                                   e => e is FFVillager && e != entity && e.Alive))
            {
                if (other is FFVillager mate && mate.Courage == EnumCourage.Bold) return true;
            }
            return false;
        }

        private Village Home
            => Villager.VillageId == 0
                ? null
                : (entity.Api as ICoreServerAPI)?.ModLoader.GetModSystem<VillageRegistry>()?.Get(Villager.VillageId);

        protected override void OnStop(bool cancelled)
        {
            // The walk belongs to ffgoto, same rule as everywhere else.
            fleeTarget = null;
        }

        public override string DebugLabel() => fighting ? "fighting back" : "fleeing";
    }
}
