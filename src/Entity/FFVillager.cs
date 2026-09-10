using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    /// <summary>
    /// A Foundries and Frontiers villager.
    ///
    /// Derived from EntityAgent rather than the base game's EntityVillager: we reuse the
    /// seraph model, its animations and its skin-part system by asset path (no art cost)
    /// but keep our own class, so we are not coupled to EntityTradingHumanoid's
    /// assumptions about inventories and dialogue.
    /// </summary>
    public class FFVillager : EntityAgent, ITalkUtil, IPathCrowdPolicy
    {
        private const string AttrTrade = "ffTrade";
        private const string AttrVillageId = "ffVillage";
        private const string AttrGivenName = "ffGivenName";
        private const string AttrAppearanceSet = "ffAppearanceSet";
        private const string AttrCulture = "ffCulture";
        private const string AttrPersonality = "ffPersonality";
        private const string AttrCourage = "ffCourage";
        private const string AttrPlotId = "ffPlot";

        /// <summary>Players this far away won't see what a villager says.</summary>
        private static double SpeechTextRange => FFConfig.Current.Chatter.SpeechTextRangeBlocks;

        // Vintage Story has no male and female voices - it has instruments and pitches, so
        // gendering a voice means biasing both pools. Only instruments whose sound file
        // actually exists in game:sounds/voice/ are listed here; the base game's skin part
        // table also offers "clarinete", which has no matching file and stays unused.
        private static readonly string[] VoicesMale   = { "tuba", "trumpet", "accordion", "harmonica" };
        private static readonly string[] VoicesFemale = { "altoflute", "oboe", "harmonica", "accordion" };

        // Hair. The base game's randomiser draws from one pool for everyone, which is how
        // you end up with bearded women in tonsures. Split by gender, with a shared middle
        // ground so the two populations still overlap and nobody looks uniform.
        private static readonly string[] HairMale = {
            "bald", "balding", "indigenousbalding", "iroquois", "tonsure", "shortspiky",
            "short-trimmed", "messy2", "kniaz",
            "afro", "classic", "combed", "dreadlocks", "layered", "messy", "short", "short-parted"
        };
        private static readonly string[] HairFemale = {
            "longflowing", "longwithstrands", "mediumlength-bangs", "longflowing", "longwithstrands",
            "afro", "classic", "combed", "dreadlocks", "layered", "messy", "short", "short-parted"
        };

        // The base randomiser is supposed to apply underwear, and evidently doesn't always
        // on a non-player entity - villagers were coming out bare. Set it explicitly.
        private static readonly string[] UnderwearMale   = { "breeches" };
        private static readonly string[] UnderwearFemale = { "twopiece", "leotard" };

        // Tied-up styles. Men mostly wear nothing extra, hence the weighting toward "none".
        private static readonly string[] HairExtraMale = {
            "none", "none", "none", "none", "none", "none",
            "classicponytail", "ponytail", "tiedback", "tieddreads", "topknot",
            "tiedtopknot", "unkempt", "vikingtopbraid", "shortmessybraid"
        };
        private static readonly string[] HairExtraFemale = {
            "none", "none",
            "backbun", "braidedup", "elaboratestickbun", "headsidebraid", "largestickbun",
            "neatbraid", "classicponytail", "ponytail", "rolledbraidwithbun", "shortmessybraid",
            "sidebraids", "sidebuns", "siderolls", "snood", "thicklongfrenchbraid",
            "tiedback", "tiedtopknot", "topbun", "vikingtopbraid"
        };

        // Repetition is the weighting. "veryhigh" is deliberately absent from both pools -
        // at 1.4x on a trumpet it is genuinely unpleasant through headphones.
        private static readonly string[] PitchMale   = { "verylow", "low", "low", "low", "medium" };
        private static readonly string[] PitchFemale = { "low", "medium", "medium", "medium", "high" };

        private EntityTalkUtil talkUtil;
        private string appliedVoiceType;
        private string appliedVoicePitch;
        private float debugLabelAccum;

        public EntityTalkUtil TalkUtil => talkUtil;

        /// <summary>
        /// The trade this villager was born to. Fixed for life - a villager can be tasked
        /// outside it during a crisis, but never reassigned. See the aptitude model.
        /// </summary>
        public EnumTrade Trade
        {
            get => (EnumTrade)WatchedAttributes.GetInt(AttrTrade, (int)EnumTrade.Forager);
            set => WatchedAttributes.SetInt(AttrTrade, (int)value);
        }

        /// <summary>
        /// Village this villager belongs to, or 0 for none. Villagers with no village
        /// are orphans: they exist, they just have nowhere to report to yet.
        ///
        /// The villager's copy is the authoritative link. The village's member list is
        /// the reverse index and gets reconciled against this when a chunk loads.
        /// </summary>
        public long VillageId
        {
            get => WatchedAttributes.GetLong(AttrVillageId, 0);
            set => WatchedAttributes.SetLong(AttrVillageId, value);
        }

        /// <summary>
        /// Which plot in their village they have been assigned to, or 0 for none.
        ///
        /// Held on the villager rather than looked up every tick because a job asks this
        /// constantly, and because a villager who has been reassigned should notice on
        /// their next thought rather than the next time somebody walks the plot list.
        /// The village's worker roster is the reverse index, same as with membership.
        /// </summary>
        public int PlotId
        {
            get => WatchedAttributes.GetInt(AttrPlotId, 0);
            private set => WatchedAttributes.SetInt(AttrPlotId, value);
        }

        public bool HasPlot => PlotId != 0;

        public void SetPlot(int plotId) => PlotId = plotId;

        public void ClearPlot() => PlotId = 0;

        /// <summary>
        /// Which personality this villager was born with. Decides how they talk and how
        /// likely they were to end up bold.
        /// </summary>
        public string PersonalityCode
        {
            get => WatchedAttributes.GetString(AttrPersonality, PersonalitySystem.DefaultCode);
            set => WatchedAttributes.SetString(AttrPersonality, value);
        }

        /// <summary>
        /// Whether they fight when hurt or run. Rolled once from their personality and
        /// then fixed, because a villager who is brave on alternate Tuesdays is not a
        /// character, it is a dice roll.
        /// </summary>
        public EnumCourage Courage
        {
            get => (EnumCourage)WatchedAttributes.GetInt(AttrCourage, (int)EnumCourage.Timid);
            set => WatchedAttributes.SetInt(AttrCourage, (int)value);
        }

        /// <summary>Who hurt them last, and when. Drives fleeing and fighting back.</summary>
        public Entity Threat { get; private set; }

        public double ThreatAtSeconds { get; private set; }

        /// <summary>True while a recent injury is still on their mind.</summary>
        public bool IsThreatened
            => Threat != null
               && Threat.Alive
               && (World.ElapsedMilliseconds / 1000.0 - ThreatAtSeconds) < FFConfig.Current.Villager.ThreatMemorySec;

        /// <summary>Seconds since the last injury, for the debug dump.</summary>
        public double ThreatAgeSec => World.ElapsedMilliseconds / 1000.0 - ThreatAtSeconds;

        public void NoteThreat(Entity from)
        {
            if (from == null || from == this) return;
            Threat = from;
            ThreatAtSeconds = World.ElapsedMilliseconds / 1000.0;
        }

        /// <summary>How readily this villager speaks, from their personality.</summary>
        public float TalkFrequency
            => Api?.ModLoader?.GetModSystem<PersonalitySystem>()?.Get(PersonalityCode)?.TalkFrequency ?? 1f;

        /// <summary>The dialogue tone this villager's personality speaks in, or empty.</summary>
        public string Tone
            => Api?.ModLoader?.GetModSystem<PersonalitySystem>()?.Get(PersonalityCode)?.Tone ?? "";

        /// <summary>
        /// Says whatever suits the moment: the weather, the hour, how they feel.
        ///
        /// Falls back to an ordinary remark when nothing in particular is going on, so
        /// this never makes them talk more than before, only about better things.
        /// </summary>
        public void SaySomethingSituational()
        {
            Culture culture = GetCulture();
            if (culture == null) { SaySomething(EnumVillagerUtterance.Remark); return; }

            string situation = PickSituation(culture);
            if (situation == null) { SaySomething(EnumVillagerUtterance.Remark); return; }

            string line = culture.RandomLine(situation, Tone, World.Rand);
            if (line == null) { SaySomething(EnumVillagerUtterance.Remark); return; }

            SayLine(line, EnumVillagerUtterance.Remark);
        }

        /// <summary>
        /// What is worth mentioning right now, most pressing first. Returns null when
        /// nothing is, which is most of the time and is the point: a villager who
        /// comments on the weather every thirty seconds is worse than one who does not.
        /// </summary>
        private string PickSituation(Culture culture)
        {
            BlockPos at = Pos.AsBlockPos;

            if (VillageSchedule.IsStorming(Api, at) && culture.HasLinesFor("storm")) return "storm";

            ITreeAttribute health = WatchedAttributes.GetTreeAttribute("health");
            if (health != null)
            {
                float max = health.GetFloat("maxhealth", 1f);
                if (max > 0 && health.GetFloat("currenthealth", max) / max < 0.5f
                    && culture.HasLinesFor("tired")) return "tired";
            }

            EnumDayPhase phase = VillageSchedule.PhaseFor(Api, at);
            double hour = World.Calendar.HourOfDay;

            if (phase == EnumDayPhase.Sleep && culture.HasLinesFor("night")) return "night";
            if (hour >= 5 && hour < 8 && culture.HasLinesFor("dawn")) return "dawn";

            // The weather is the fallback subject, the way it is for everyone.
            var climate = Api.World.BlockAccessor.GetClimateAt(at);
            if (climate != null)
            {
                if (climate.Rainfall > 0.15f)
                {
                    bool freezing = climate.Temperature < 0;
                    string key = freezing ? "snow" : "rain";
                    if (culture.HasLinesFor(key)) return key;
                }
                if (climate.Temperature > 28 && culture.HasLinesFor("hot")) return "hot";
            }

            return null;
        }

        public void ForgetThreat()
        {
            Threat = null;
        }

        /// <summary>
        /// Remember who did this. The reaction task reads it and decides whether this
        /// villager is the sort to run or the sort to swing back.
        /// </summary>
        public override bool ReceiveDamage(DamageSource damageSource, float damage)
        {
            bool taken = base.ReceiveDamage(damageSource, damage);

            if (taken && World?.Side == EnumAppSide.Server && damage > 0)
            {
                Entity from = damageSource?.SourceEntity ?? damageSource?.CauseEntity;
                if (from != null && from != this) NoteThreat(from);
            }

            return taken;
        }

        /// <summary>Personal name, shown on the nametag and used in the village event log.</summary>
        public string GivenName
        {
            get => WatchedAttributes.GetString(AttrGivenName, "");
            set
            {
                WatchedAttributes.SetString(AttrGivenName, value);
                RefreshNameTag();
            }
        }

        public bool IsFemale => Code?.Path?.Contains("-female-") == true;

        // --- movement orders -------------------------------------------------------
        // Runtime only, deliberately not persisted: a half-finished journey is not worth
        // restoring across a reload, and whatever issued the order will reissue it.

        /// <summary>Where this villager is currently trying to walk to, if anywhere.</summary>
        public BlockPos GotoTarget { get; private set; }

        /// <summary>Speed for the current order. Pick from MoveSpeeds.</summary>
        public float GotoSpeed { get; private set; } = MoveSpeeds.Walk;

        /// <summary>Set when the last order finished, for callers and the debug overlay.</summary>
        public string LastGotoResult { get; private set; } = "none";

        /// <summary>
        /// Crowd avoidance stays on for ordinary work. It gets switched off for things
        /// where villagers legitimately need to bunch up - mustering, sheltering, a gate
        /// during a raid - none of which exist yet.
        /// </summary>
        public bool IgnoreCrowding => false;

        public void OrderGoto(BlockPos target, float speed)
        {
            GotoTarget = target?.Copy();
            GotoSpeed = speed;
            LastGotoResult = "walking";
        }

        public void CancelGoto()
        {
            GotoTarget = null;
            LastGotoResult = "cancelled";
        }

        public void OnGotoArrived()
        {
            GotoTarget = null;
            LastGotoResult = "arrived";
        }

        public void OnGotoFailed(string reason)
        {
            GotoTarget = null;
            LastGotoResult = "failed: " + reason;
        }

        // --- carrying and tools ----------------------------------------------------
        //
        // EntityAgent declares LeftHandItemSlot and RightHandItemSlot as plain auto
        // properties that default to NULL - nothing creates them for you. Assigning to
        // them without creating them first silently does nothing, which is exactly the
        // bug that made villagers report carrying things they weren't.
        //
        // The slots are created here, and the stacks are mirrored into WatchedAttributes
        // so they persist across a save and sync to clients for rendering. The attribute
        // tree is the source of truth; the slots are the view the engine renders from.

        private const string AttrHands = "ffHands";
        private const string HandCarried = "carried";
        private const string HandTool = "tool";

        public ItemStack CarriedStack
        {
            get => LeftHandItemSlot?.Itemstack;
            private set => SetHand(HandCarried, LeftHandItemSlot, value);
        }

        /// <summary>What they work with. Its tier multiplies their work rate.</summary>
        public ItemStack ToolStack
        {
            get => RightHandItemSlot?.Itemstack;
            private set => SetHand(HandTool, RightHandItemSlot, value);
        }

        private void SetHand(string key, ItemSlot slot, ItemStack stack)
        {
            if (slot == null) return;

            slot.Itemstack = stack;
            slot.MarkDirty();

            ITreeAttribute hands = WatchedAttributes.GetOrAddTreeAttribute(AttrHands);
            if (stack == null) hands.RemoveAttribute(key);
            else hands.SetItemstack(key, stack.Clone());

            WatchedAttributes.MarkPathDirty(AttrHands);
        }

        /// <summary>
        /// Writes whatever is actually in the hands back into the attributes.
        ///
        /// Needed because the game can change a held stack without going through the
        /// setters above: an axe swung at a tree takes durability damage and eventually
        /// breaks, and both of those happen on the slot. Without this the attribute copy
        /// still describes the axe as it was, so a reload hands the villager back a
        /// pristine axe, or an axe that broke an hour ago.
        /// </summary>
        public void RefreshHands()
        {
            ITreeAttribute hands = WatchedAttributes.GetOrAddTreeAttribute(AttrHands);

            Mirror(hands, HandCarried, LeftHandItemSlot?.Itemstack);
            Mirror(hands, HandTool, RightHandItemSlot?.Itemstack);

            WatchedAttributes.MarkPathDirty(AttrHands);
        }

        private static void Mirror(ITreeAttribute hands, string key, ItemStack stack)
        {
            if (stack == null || stack.StackSize <= 0) hands.RemoveAttribute(key);
            else hands.SetItemstack(key, stack.Clone());
        }

        /// <summary>Rebuilds the hand slots from the synced attributes.</summary>
        private void ReadHandsFromAttributes()
        {
            if (LeftHandItemSlot == null || RightHandItemSlot == null) return;

            ITreeAttribute hands = WatchedAttributes.GetTreeAttribute(AttrHands);

            ItemStack carried = hands?.GetItemstack(HandCarried);
            carried?.ResolveBlockOrItem(World);
            LeftHandItemSlot.Itemstack = carried;

            ItemStack tool = hands?.GetItemstack(HandTool);
            tool?.ResolveBlockOrItem(World);
            RightHandItemSlot.Itemstack = tool;
        }

        public bool IsCarrying => CarriedStack != null && CarriedStack.StackSize > 0;

        public int CarriedCount => CarriedStack?.StackSize ?? 0;

        /// <summary>Tier of the held tool, 0 for none.</summary>
        public int ToolTier => VillagerCarry.ToolTierOf(ToolStack);

        /// <summary>
        /// How fast this villager works right now, before aptitude and health are applied.
        /// Those arrive with the labour allocator; this is the tool half of the equation.
        /// </summary>
        public float WorkRate => VillagerCarry.WorkRateForToolTier(ToolTier);

        /// <summary>
        /// Picks up as much as will fit, and reports what could not be taken so the caller
        /// knows whether to make a second trip.
        /// </summary>
        public int TryCarry(ItemStack stack)
        {
            if (stack == null || stack.StackSize <= 0) return 0;

            ItemStack held = CarriedStack;
            if (held != null && !held.Satisfies(stack)) return 0;   // hands already full of something else

            int already = held?.StackSize ?? 0;
            int room = VillagerCarry.CarryCapacity - already;
            if (room <= 0) return 0;

            int taken = Math.Min(room, stack.StackSize);

            if (held == null)
            {
                ItemStack copy = stack.Clone();
                copy.StackSize = taken;
                CarriedStack = copy;

                // Report what actually happened, not what we intended. The previous version
                // returned `taken` unconditionally and cheerfully claimed to be carrying
                // things it had dropped on the floor of a null slot.
                return CarriedStack?.StackSize ?? 0;
            }

            held.StackSize += taken;
            SetHand(HandCarried, LeftHandItemSlot, held);
            return taken;
        }

        /// <summary>Hands over everything carried and empties the hands.</summary>
        public ItemStack TakeCarried()
        {
            ItemStack held = CarriedStack;
            CarriedStack = null;
            return held;
        }

        public void GiveTool(ItemStack tool)
        {
            ToolStack = tool;
        }

        /// <summary>Which culture this villager belongs to. Decides their name and their voice.</summary>
        public string CultureCode
        {
            get => WatchedAttributes.GetString(AttrCulture, CultureSystem.DefaultCulture);
            set => WatchedAttributes.SetString(AttrCulture, value);
        }

        public Culture GetCulture()
            => Api?.ModLoader?.GetModSystem<CultureSystem>()?.Get(CultureCode);

        /// <summary>
        /// Writes the floating tag above the villager's head.
        ///
        /// The tag is the only text the game shows on hover - GetInfoText below is not
        /// surfaced for entities outside debug views - so culture and trade have to go
        /// here if they are to be visible at all.
        /// </summary>
        public void RefreshNameTag()
        {
            var tag = GetBehavior<EntityBehaviorNameTag>();
            if (tag == null) return;

            var mod = Api?.ModLoader?.GetModSystem<FoundriesFrontiersMod>();
            if (mod != null && mod.DebugLabels)
            {
                tag.SetName((GivenName == "" ? "#" + EntityId : GivenName) + "\n" + DebugState());
                return;
            }

            string name = GivenName;
            if (name == "") { tag.SetName(""); return; }

            Culture culture = GetCulture();
            string cultureName = culture?.DisplayName ?? CultureCode;

            tag.SetName(name + "\n" + cultureName + " " + Trade.ToString().ToLowerInvariant());
        }

        /// <summary>
        /// One line describing what this villager is doing right now. Grows as the AI does -
        /// at this stage there is no AI, so it reports the little there is.
        /// </summary>
        public string DebugState()
        {
            if (!Alive) return "dead";

            var chatter = GetBehavior<EntityBehaviorVillagerChatter>();
            if (chatter != null && chatter.IsTalking) return "talking";

            var taskai = GetBehavior<EntityBehaviorTaskAI>();
            var active = taskai?.TaskManager?.ActiveTasksBySlot;
            if (active != null)
            {
                foreach (var t in active)
                {
                    if (t == null) continue;
                    if (t is FFTaskBase ff) return ff.DebugLabel();
                    return t.GetType().Name.Replace("AiTask", "").ToLowerInvariant();
                }
            }

            if (Controls != null && Controls.TriesToMove) return "walking";
            return "idle";
        }

        /// <summary>
        /// Our own entity packet id. The engine uses 1 and 196-203; anything up here is ours.
        /// </summary>
        public const int PacketIdUtterance = 1701;

        /// <summary>
        /// Server-side: make this villager say something, in their own voice, for everyone
        /// nearby. The client decides how it actually sounds.
        /// </summary>
        public void SaySomething(EnumVillagerUtterance utterance)
        {
            if (World.Side != EnumAppSide.Server || !Alive) return;

            ICoreServerAPI sapi = World.Api as ICoreServerAPI;
            sapi.Network.BroadcastEntityPacket(EntityId, PacketIdUtterance, new byte[] { (byte)utterance });

            SendSpeechText(sapi, utterance);
        }

        /// <summary>
        /// Puts what the villager said into the chat of anyone close enough to hear it.
        /// Deliberately range-gated - a village of thirty should not fill the log for
        /// someone standing on a hill half a mile off.
        /// </summary>
        private void SendSpeechText(ICoreServerAPI sapi, EnumVillagerUtterance utterance)
        {
            Culture culture = GetCulture();
            if (culture == null) return;

            SendSpeechLine(sapi, culture.RandomLine(utterance.ToString().ToLowerInvariant(), Tone, World.Rand));
        }

        /// <summary>Says one specific line, in the voice they would have said it.</summary>
        public void SayLine(string line, EnumVillagerUtterance voice)
        {
            if (World.Side != EnumAppSide.Server || !Alive) return;

            ICoreServerAPI sapi = World.Api as ICoreServerAPI;
            sapi.Network.BroadcastEntityPacket(EntityId, PacketIdUtterance, new byte[] { (byte)voice });
            SendSpeechLine(sapi, line);
        }

        private void SendSpeechLine(ICoreServerAPI sapi, string line)
        {
            if (!FFConfig.Current.Chatter.SpeechInChat) return;
            if (string.IsNullOrEmpty(line)) return;

            string speaker = GivenName != "" ? GivenName : "Villager";
            string message = speaker + ": " + line;

            foreach (IPlayer plr in World.AllOnlinePlayers)
            {
                if (!(plr is IServerPlayer splr) || splr.Entity == null) continue;
                if (splr.Entity.Pos.SquareDistanceTo(Pos.XYZ) > SpeechTextRange * SpeechTextRange) continue;

                sapi.SendMessage(splr, GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }
        }

        private static EnumTalkType ToTalkType(EnumVillagerUtterance utterance)
        {
            switch (utterance)
            {
                case EnumVillagerUtterance.Greet:    return EnumTalkType.Meet;
                case EnumVillagerUtterance.Remark:   return EnumTalkType.IdleShort;
                case EnumVillagerUtterance.Laugh:    return EnumTalkType.Laugh;
                case EnumVillagerUtterance.Shrug:    return EnumTalkType.Shrug;
                case EnumVillagerUtterance.Complain: return EnumTalkType.Complain;
                case EnumVillagerUtterance.Hurt:     return EnumTalkType.Hurt;
                case EnumVillagerUtterance.Death:    return EnumTalkType.Death;
                default:                             return EnumTalkType.Idle;
            }
        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);

            // Must exist on both sides: the server writes them, the client renders them.
            LeftHandItemSlot = new DummySlot();
            RightHandItemSlot = new DummySlot();
            ReadHandsFromAttributes();
            WatchedAttributes.RegisterModifiedListener(AttrHands, ReadHandsFromAttributes);

            if (api.Side == EnumAppSide.Client)
            {
                // Seraph voices are single ogg files rather than folders of them,
                // hence isMultiSoundVoice: false.
                talkUtil = new EntityTalkUtil(api as ICoreClientAPI, this, false);

                // Well below the engine default of 0.0005. One villager muttering every
                // half-minute sounds alive; ten of them doing it sounds like a machine
                // shop. Ambient noise scales with village size, so this has to be sparse.
                talkUtil.ShouldDoIdleTalk = true;
                // Scaled by personality, so a quiet villager really is quieter and a warm
                // one really is chattier, without anybody becoming exhausting.
                talkUtil.idleTalkChance =
                    FFConfig.Current.Chatter.IdleTalkChance * TalkFrequency;

                // Villagers should sit under the ambience, not on top of it.
                talkUtil.volumneModifier = FFConfig.Current.Chatter.VoiceVolume;

                RefreshVoice();
                return;
            }

            if (!WatchedAttributes.HasAttribute(AttrTrade))
            {
                Trade = TradeFromCode(Code?.Path);
            }

            if (GivenName == "")
            {
                Culture culture = GetCulture();
                if (culture != null) GivenName = culture.RandomName(IsFemale, World.Rand);
            }
            else
            {
                // Nametag behaviour is rebuilt on load, so re-apply the stored name.
                RefreshNameTag();
            }
        }

        /// <summary>
        /// Appearance is applied here rather than in Initialize, and the ordering is the
        /// whole point.
        ///
        /// `extraskinnable` randomises its parts at the end of its own Initialize, but its
        /// real setup, `init()`, does not run until OnEntitySpawn. Applying our
        /// overrides during Initialize therefore landed in a window where they could be
        /// undone, which showed up as villagers spawning with the wrong hair, no
        /// underwear, or a randomly-gendered voice, seemingly at random.
        /// </summary>
        public override void OnEntitySpawn()
        {
            base.OnEntitySpawn();
            EnsureAppearance();
        }

        public override void OnEntityLoaded()
        {
            base.OnEntityLoaded();
            EnsureAppearance();
        }

        private void EnsureAppearance()
        {
            if (World?.Side != EnumAppSide.Server) return;
            if (WatchedAttributes.GetBool(AttrAppearanceSet, false)) return;

            ApplyGenderedAppearance();
            DressFromWardrobe();
            WatchedAttributes.SetBool(AttrAppearanceSet, true);

            // Who they are, rolled once and kept. Personality decides how they talk and
            // how likely they were to end up brave; courage is then fixed, because a
            // villager who is brave on alternate days is a dice roll, not a character.
            var personalities = Api?.ModLoader?.GetModSystem<PersonalitySystem>();
            if (personalities != null && WatchedAttributes.GetString(AttrPersonality, null) == null)
            {
                string rolled = personalities.Roll(World.Rand);
                PersonalityCode = rolled;
                Courage = personalities.RollCourage(rolled, Trade, World.Rand);
            }

            GiveTradeTool();
        }

        /// <summary>
        /// Hands a villager the basic tool their trade works with, if they have none.
        ///
        /// Not a convenience. The game puts real behaviour on tools rather than just a
        /// speed bonus: an axe fells a whole tree where bare hands take one log off it,
        /// and a lumberjack with nothing in their hands is a lumberjack who cannot do the
        /// job at all. Until the craft chain exists to make them, the village is assumed
        /// to have managed the simplest one.
        ///
        /// The tool is the lowest tier the game offers, found by asking the registry, so
        /// it is a starting point rather than a gift and a modded axe works as well as a
        /// vanilla one. Turn it off with GiveTradeToolsOnSpawn.
        /// </summary>
        private void GiveTradeTool()
        {
            if (Api?.Side != EnumAppSide.Server) return;
            if (!FFConfig.Current.Villager.GiveTradeToolsOnSpawn) return;
            if (ToolStack != null) return;

            Item tool = Api.ModLoader.GetModSystem<WorldCatalogue>()?.ToolFor(Trade);
            if (tool == null) return;

            GiveTool(new ItemStack(tool));
        }

        /// <summary>
        /// Puts actual clothes on the villager, drawn from their culture's wardrobe.
        ///
        /// Underwear is a skin-part texture; this is real wearable items in the gear
        /// inventory, which is what the base game's own NPCs use. Because the wardrobe is
        /// data, a Norse villager comes out in fur boots and hide trousers while a Norman
        /// gets a linen tunic and shoes, with no code aware that either culture exists.
        /// </summary>
        private void DressFromWardrobe()
        {
            Culture culture = GetCulture();
            if (culture == null) return;

            var gear = GetBehavior<EntityBehaviorSeraphInventory>();
            IInventory inv = gear?.Inventory;
            if (inv == null) return;

            foreach (string slotName in ClothingSlots)
            {
                string code = culture.RandomGarment(slotName, World.Rand);
                if (string.IsNullOrEmpty(code)) continue;

                Item item = World.GetItem(new AssetLocation("game", code));
                if (item == null)
                {
                    Api.Logger.Warning("[F&F] Wardrobe item '{0}' for culture '{1}' does not resolve.",
                        code, CultureCode);
                    continue;
                }

                var stack = new ItemStack(item);
                var source = new DummySlot(stack);

                // Let the gear inventory decide which slot a garment belongs in rather than
                // hardcoding indices - the mapping is the item's business, not ours.
                WeightedSlot best = inv.GetBestSuitedSlot(source);
                if (best?.slot != null && best.slot.Empty)
                {
                    best.slot.Itemstack = stack;
                    best.slot.MarkDirty();
                }
            }

            MarkShapeModified();
        }

        private static readonly string[] ClothingSlots = { "upperbody", "lowerbody", "foot" };

        private void ApplyGenderedAppearance()
        {
            EntityBehaviorExtraSkinnable skin = GetBehavior<EntityBehaviorExtraSkinnable>();
            if (skin == null) return;

            bool female = IsFemale;
            Random rand = World.Rand;

            string[] voices = female ? VoicesFemale : VoicesMale;
            string[] pitches = female ? PitchFemale : PitchMale;

            // Voice parts never change the mesh, so no retesselation and no bleat on spawn.
            skin.selectSkinPart("voicetype", voices[rand.Next(voices.Length)], false, false);
            skin.selectSkinPart("voicepitch", pitches[rand.Next(pitches.Length)], false, false);

            string[] underwear = female ? UnderwearFemale : UnderwearMale;
            skin.selectSkinPart("underwear", underwear[rand.Next(underwear.Length)], false, false);

            string[] hair = female ? HairFemale : HairMale;
            string[] hairExtra = female ? HairExtraFemale : HairExtraMale;

            skin.selectSkinPart("hairbase", hair[rand.Next(hair.Length)], false, false);
            skin.selectSkinPart("hairextra", hairExtra[rand.Next(hairExtra.Length)], false, false);

            if (female)
            {
                skin.selectSkinPart("beard", "none", false, false);
                skin.selectSkinPart("mustache", "none", false, false);
            }

            // One retesselation for the whole set rather than one per part.
            MarkShapeModified();
        }

        /// <summary>
        /// Points the talk util at this villager's instrument and pitch.
        ///
        /// The skin behaviour's own ApplyVoice is hardcoded to EntityPlayer, so it silently
        /// does nothing for us - we have to read the chosen parts back out of the watched
        /// attributes and drive the talk util ourselves.
        /// </summary>
        private void RefreshVoice()
        {
            if (talkUtil == null) return;

            string voiceType = WatchedAttributes.GetString("voicetype", "altoflute");
            string voicePitch = WatchedAttributes.GetString("voicepitch", "medium");

            if (voiceType == appliedVoiceType && voicePitch == appliedVoicePitch) return;
            appliedVoiceType = voiceType;
            appliedVoicePitch = voicePitch;

            talkUtil.soundName = new AssetLocation("game", "sounds/voice/" + voiceType);
            talkUtil.pitchModifier = PitchModifierFor(voicePitch);
            talkUtil.chordDelayMul = 1.1f;
        }

        /// <summary>
        /// Deliberately gentler than the engine's own mapping, which runs 0.6 to 1.4.
        /// Anything above about 1.15 on a brass voice is piercing, and a village means
        /// hearing a lot of these at once.
        /// </summary>
        private static float PitchModifierFor(string pitch)
        {
            switch (pitch)
            {
                case "verylow": return 0.70f;
                case "low": return 0.85f;
                case "medium": return 0.98f;
                case "high": return 1.10f;
                case "veryhigh": return 1.15f;
                default: return 0.98f;
            }
        }

        // --- debug gait driving ----------------------------------------------------
        private float driveRemaining;
        private float driveSpeed;
        private string driveAnim;

        /// <summary>Test hook: walk forward at a set speed for a few seconds.</summary>
        public void DebugDrive(float speed, float seconds)
        {
            driveSpeed = speed;
            driveRemaining = seconds;
            driveAnim = MoveSpeeds.AnimationFor(speed);
            AnimManager?.StartAnimation(driveAnim);
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);

            if (driveRemaining > 0 && World.Side == EnumAppSide.Server)
            {
                driveRemaining -= dt;
                Controls.Forward = true;
                Controls.WalkVector.Set(
                    Math.Sin(Pos.Yaw) * driveSpeed, 0, Math.Cos(Pos.Yaw) * driveSpeed);

                if (driveRemaining <= 0)
                {
                    Controls.Forward = false;
                    Controls.WalkVector.Set(0, 0, 0);
                    AnimManager?.StopAnimation(driveAnim);
                }
            }

            if (World.Side == EnumAppSide.Client)
            {
                RefreshVoice();
                talkUtil?.OnGameTick(dt);
                return;
            }

            // Debug labels have to be pushed as state changes, but once a second is
            // plenty and keeps it off the hot path.
            var mod = Api?.ModLoader?.GetModSystem<FoundriesFrontiersMod>();
            if (mod != null && mod.DebugLabels)
            {
                debugLabelAccum += dt;
                if (debugLabelAccum >= 1f)
                {
                    debugLabelAccum = 0;
                    RefreshNameTag();
                }
            }
        }

        /// <summary>
        /// Suppress the seraph's generic grunt and send a packet instead, so the client can
        /// answer in this villager's own voice. Without this every villager, female
        /// included, makes the same male noise.
        /// </summary>
        public override void PlayEntitySound(string type, IPlayer dualCallByPlayer = null)
        {
            if (World.Side == EnumAppSide.Server)
            {
                if (type == "hurt")
                {
                    (World.Api as ICoreServerAPI).Network
                        .BroadcastEntityPacket(EntityId, (int)EntityServerPacketId.Hurt);
                    return;
                }
                if (type == "death")
                {
                    (World.Api as ICoreServerAPI).Network
                        .BroadcastEntityPacket(EntityId, (int)EntityServerPacketId.Death);
                    return;
                }
            }

            base.PlayEntitySound(type, dualCallByPlayer);
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            base.OnReceivedServerPacket(packetid, data);

            if (packetid == (int)EntityServerPacketId.Hurt)
            {
                if (Alive) talkUtil?.Talk(EnumTalkType.Hurt);
            }
            else if (packetid == (int)EntityServerPacketId.Death)
            {
                talkUtil?.Talk(EnumTalkType.Death);
            }
            else if (packetid == PacketIdUtterance && data != null && data.Length > 0)
            {
                talkUtil?.Talk(ToTalkType((EnumVillagerUtterance)data[0]));
            }
        }

        private static EnumTrade TradeFromCode(string path)
        {
            if (path == null) return EnumTrade.Forager;
            foreach (EnumTrade t in Enum.GetValues<EnumTrade>())
            {
                if (path.EndsWith("-" + t.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                {
                    return t;
                }
            }
            return EnumTrade.Forager;
        }

        public override string GetInfoText()
        {
            string text = base.GetInfoText();
            text += "\nTrade: " + Trade;
            if (IsCarrying) text += "\nCarrying: " + CarriedCount + "x " + CarriedStack.GetName();
            if (ToolStack != null) text += "\nTool: " + ToolStack.GetName() + " (tier " + ToolTier + ")";
            text += "\nCulture: " + (GetCulture()?.DisplayName ?? CultureCode);
            text += "\nGender: " + (IsFemale ? "female" : "male");
            text += "\nVoice: " + WatchedAttributes.GetString("voicetype", "?")
                  + " (" + WatchedAttributes.GetString("voicepitch", "?") + ")";
            if (VillageId != 0) text += "\nVillage: #" + VillageId;
            return text;
        }
    }
}
