using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.MathTools;

namespace FoundriesFrontiers
{
    public enum EnumBuildState
    {
        /// <summary>Chosen, sited, and waiting on materials.</summary>
        Waiting = 0,

        /// <summary>Paid for and going up.</summary>
        Building = 1,

        /// <summary>Standing.</summary>
        Done = 2,

        /// <summary>Given up on. Whatever was placed stays where it is.</summary>
        Abandoned = 3
    }

    /// <summary>
    /// A building the village has decided on, and how far along it is.
    ///
    /// Progress is a block count rather than a percentage, because the thing that
    /// actually happens is that a builder places blocks one at a time and the count is
    /// where they got to. A percentage would be a number derived from that and then
    /// trusted instead of it, which is the shape of most of the bugs this project has
    /// had.
    ///
    /// Materials are paid for **up front, once**, when the site moves out of Waiting.
    /// That is deliberate. Paying per block would mean a village that runs dry halfway
    /// leaves a half built shell it can never finish and can never recover the stone
    /// from, and the design's rule is that a stalled build stalls before it starts.
    /// </summary>
    public class VillageBuildSite
    {
        [JsonProperty] public int Id;

        /// <summary>Schematic code, which is the key into the catalogue.</summary>
        [JsonProperty] public string PlanCode = "";

        [JsonProperty] public EnumBuildState State = EnumBuildState.Waiting;

        /// <summary>North west bottom corner, where the schematic is placed from.</summary>
        [JsonProperty] public int X;
        [JsonProperty] public int Y;
        [JsonProperty] public int Z;

        /// <summary>How many blocks of the schematic have been placed.</summary>
        [JsonProperty] public int Placed;

        /// <summary>How many there are to place. Copied from the plan when the site is made.</summary>
        [JsonProperty] public int Total;

        /// <summary>
        /// How long the plan's layout was when this site was created.
        ///
        /// Placed is an index into a list that is rebuilt from the world's block registry
        /// every startup, so its length can change under a saved site: install another
        /// mod, or re-export the schematic, and the cursor is pointing at something else.
        /// Recording the length is what lets that be noticed rather than quietly marking a
        /// third built house finished.
        /// </summary>
        [JsonProperty] public int LayoutCount;

        /// <summary>
        /// Which way this building faces, in degrees clockwise from the schematic's own
        /// orientation: 0, 90, 180 or 270.
        ///
        /// Schematics are all built and exported facing north, and for a long time that
        /// was also how they were placed, which gave every village a street of houses all
        /// staring the same way regardless of what they stood next to. The rotation is
        /// chosen when the site is sited, from where the building sits relative to the
        /// square, and stored here so a half built house does not turn round on reload.
        /// </summary>
        [JsonProperty] public int Rotation;

        /// <summary>Whether the materials have been taken out of the ledger yet.</summary>
        [JsonProperty] public bool Paid;

        [JsonProperty] public double StartedTotalDays;

        /// <summary>Who is working on it, or 0 for nobody.</summary>
        [JsonProperty] public long BuilderEntityId;

        /// <summary>
        /// Why it is not progressing, in words, for the block's tooltip and the command.
        /// Set every time something refuses, so a stalled site says what it is short of
        /// rather than simply sitting there.
        /// </summary>
        [JsonProperty] public string Holdup = "";

        [JsonIgnore] public BlockPos Origin => new BlockPos(X, Y, Z, 0);

        [JsonIgnore] public bool IsOpen => State == EnumBuildState.Waiting || State == EnumBuildState.Building;

        [JsonIgnore]
        public float Progress => Total <= 0 ? 0 : GameMath.Clamp(Placed / (float)Total, 0, 1);

        public override string ToString()
            => "#" + Id + " " + PlanCode + " at " + Origin
             + ", facing " + Facing
             + ", " + State.ToString().ToLowerInvariant()
             + ", " + Placed + "/" + Total
             + (Holdup == "" ? "" : " (" + Holdup + ")");

        /// <summary>
        /// Which way the front of this building points, in words.
        ///
        /// Schematics are exported facing north, so an unrotated one faces north and each
        /// quarter turn clockwise carries the front round with it.
        /// </summary>
        [JsonIgnore] public string Facing
        {
            get
            {
                switch (((Rotation / 90 % 4) + 4) % 4)
                {
                    case 1: return "east";
                    case 2: return "south";
                    case 3: return "west";
                    default: return "north";
                }
            }
        }
    }
}
