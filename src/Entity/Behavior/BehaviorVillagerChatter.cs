using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Ambient social behaviour: villagers greet players who approach and hold short
    /// exchanges with each other.
    ///
    /// Tuned sparse on purpose. Every number here is per villager, so anything that sounds
    /// right for one is roughly N times too much in a village of N. Erring quiet.
    ///
    /// This is deliberately not an AI task. It runs alongside whatever a villager is doing
    /// rather than competing with it for a task slot, so a farmer can pass the time of day
    /// with a neighbour without stopping work. When real jobs arrive this keeps working
    /// unchanged.
    ///
    /// Server-authoritative: the server decides who speaks and when, then broadcasts, so
    /// both halves of a conversation stay in sync for every client watching.
    /// </summary>
    public class EntityBehaviorVillagerChatter : EntityBehavior
    {
        // How close a player has to come before being noticed.
        private static float GreetRange => FFConfig.Current.Chatter.GreetRangeBlocks;
        private static double GreetCooldownSec => FFConfig.Current.Chatter.GreetCooldownSec;

        // Conversation partners must be at least this close, and it ends if they drift apart.
        private static float ChatRange => FFConfig.Current.Chatter.ChatRangeBlocks;
        private static double ChatCooldownSec => FFConfig.Current.Chatter.ChatCooldownSec;
        private static double ChatStartChancePerCheck => FFConfig.Current.Chatter.ChatStartChance;

        private const float PlayerScanIntervalSec = 1.0f;
        private const float SocialScanIntervalSec = 6.0f;

        private float playerScanAccum;
        private float socialScanAccum;

        private double lastGreetTotalSec;
        private double lastChatTotalSec;

        // Set while mid-exchange so two villagers don't talk over each other.
        private double busyUntilTotalSec;
        private long partnerEntityId;
        private int exchangesLeft;
        private double nextLineAtTotalSec;

        private FFVillager villager;

        public EntityBehaviorVillagerChatter(Entity entity) : base(entity) { }

        public override string PropertyName() => "ffchatter";

        /// <summary>True while mid-exchange, for the debug overlay.</summary>
        public bool IsTalking => exchangesLeft > 0;

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            villager = entity as FFVillager;
        }

        public override void OnGameTick(float deltaTime)
        {
            base.OnGameTick(deltaTime);

            if (villager == null || entity.World.Side != EnumAppSide.Server) return;
            if (!entity.Alive) return;

            double now = entity.World.Calendar.ElapsedSeconds;

            // An exchange already in progress takes precedence over starting anything new.
            if (exchangesLeft > 0)
            {
                ContinueConversation(now);
                return;
            }

            playerScanAccum += deltaTime;
            if (playerScanAccum >= PlayerScanIntervalSec)
            {
                playerScanAccum = 0;
                TryGreetPlayer(now);
            }

            socialScanAccum += deltaTime;
            if (socialScanAccum >= SocialScanIntervalSec)
            {
                socialScanAccum = 0;
                TryStartConversation(now);
            }
        }

        private void TryGreetPlayer(double now)
        {
            if (now - lastGreetTotalSec < GreetCooldownSec) return;
            if (now < busyUntilTotalSec) return;

            IPlayer nearest = entity.World.NearestPlayer(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
            if (nearest?.Entity == null) return;

            double distSq = nearest.Entity.Pos.SquareDistanceTo(entity.Pos.XYZ);
            if (distSq > GreetRange * GreetRange) return;

            lastGreetTotalSec = now;
            busyUntilTotalSec = now + 2;

            FaceTowards(nearest.Entity.Pos.XYZ);
            villager.SaySomething(EnumVillagerUtterance.Greet);
        }

        private void TryStartConversation(double now)
        {
            if (now - lastChatTotalSec < ChatCooldownSec) return;
            if (now < busyUntilTotalSec) return;
            if (entity.World.Rand.NextDouble() > ChatStartChancePerCheck) return;

            // Only bother if a player is close enough to hear it. Villages left alone
            // in loaded chunks shouldn't burn ticks chatting to an empty field.
            IPlayer witness = entity.World.NearestPlayer(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
            if (witness?.Entity == null) return;
            float witnessRange = FFConfig.Current.Chatter.ChatWitnessRangeBlocks;
            if (witness.Entity.Pos.SquareDistanceTo(entity.Pos.XYZ) > witnessRange * witnessRange) return;

            Entity found = entity.World.GetNearestEntity(entity.Pos.XYZ, ChatRange, ChatRange, e =>
            {
                if (e.EntityId == entity.EntityId) return false;
                if (!(e is FFVillager other) || !other.Alive) return false;

                var otherChatter = other.GetBehavior<EntityBehaviorVillagerChatter>();
                return otherChatter != null && otherChatter.IsAvailableToTalk(now);
            });

            if (!(found is FFVillager partner)) return;

            var partnerChatter = partner.GetBehavior<EntityBehaviorVillagerChatter>();
            if (partnerChatter == null) return;

            // Two to four lines, alternating. We open.
            int lines = 2 + entity.World.Rand.Next(2);

            BeginConversation(partner.EntityId, lines, now, speakingFirst: true);
            partnerChatter.BeginConversation(entity.EntityId, lines, now, speakingFirst: false);
        }

        /// <summary>True when this villager is free to be drawn into a conversation.</summary>
        public bool IsAvailableToTalk(double now)
        {
            return exchangesLeft == 0
                && now >= busyUntilTotalSec
                && now - lastChatTotalSec >= ChatCooldownSec;
        }

        public void BeginConversation(long partnerId, int lines, double now, bool speakingFirst)
        {
            partnerEntityId = partnerId;
            exchangesLeft = lines;
            lastChatTotalSec = now;

            // The opener speaks almost immediately; the other waits for their turn.
            nextLineAtTotalSec = now + (speakingFirst ? 0.3 : 2.2);

            Entity partner = entity.World.GetEntityById(partnerId);
            if (partner != null) FaceTowards(partner.Pos.XYZ);
        }

        private void ContinueConversation(double now)
        {
            Entity partner = entity.World.GetEntityById(partnerEntityId);

            // Partner gone, dead or wandered off - trail away rather than talking to nobody.
            if (partner == null || !partner.Alive
                || partner.Pos.SquareDistanceTo(entity.Pos.XYZ) > (ChatRange + 3) * (ChatRange + 3))
            {
                EndConversation(now);
                return;
            }

            if (now < nextLineAtTotalSec) return;

            FaceTowards(partner.Pos.XYZ);

            // A short remark most of the time, occasionally a laugh or a shrug.
            double roll = entity.World.Rand.NextDouble();
            // Mostly they say something about the moment: the weather, the hour, how
            // they feel. Laughing and shrugging stay in as punctuation. This does not
            // make them talk more, only about better things.
            if (roll < 0.12) villager.SaySomething(EnumVillagerUtterance.Laugh);
            else if (roll < 0.20) villager.SaySomething(EnumVillagerUtterance.Shrug);
            else villager.SaySomethingSituational();

            exchangesLeft--;
            // Each side speaks on alternate beats, so wait out the partner's turn plus a pause.
            nextLineAtTotalSec = now + 3.2 + entity.World.Rand.NextDouble() * 1.6;

            if (exchangesLeft <= 0) EndConversation(now);
        }

        private void EndConversation(double now)
        {
            exchangesLeft = 0;
            partnerEntityId = 0;
            lastChatTotalSec = now;
            busyUntilTotalSec = now + 2;
        }

        private void FaceTowards(Vec3d target)
        {
            double dx = target.X - entity.Pos.X;
            double dz = target.Z - entity.Pos.Z;
            if (dx * dx + dz * dz < 0.01) return;

            float yaw = (float)Math.Atan2(dx, dz);
            entity.Pos.Yaw = yaw;
            if (entity is EntityAgent agent)
            {
                agent.BodyYaw = yaw;
                agent.BodyYawServer = yaw;
            }
        }
    }
}
