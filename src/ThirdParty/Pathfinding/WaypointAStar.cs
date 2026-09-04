// Adapted from VS Village (https://github.com/G3rste/vsvillage), MIT licensed.
// See src/ThirdParty/LICENSE-vsvillage.txt. Changes: namespace, the coupling to
// VS Village's own village types replaced with IPathCrowdPolicy, instrumentation.

using Vintagestory.API.Common;

namespace FoundriesFrontiers;

public class WaypointAStar : VillagerAStarNew
{
	public WaypointAStar(ICachingBlockAccessor blockAccessor, IWorldAccessor world)
		: base(blockAccessor, world)
	{
	}

	protected bool canStep(Block belowBlock)
	{
		return steppableCodes.Exists((string code) => belowBlock.Code.Path.Contains(code));
	}
}
