using UdonSharp;
using VRC.SDKBase;

namespace LoopLand
{
    /// <summary>Lives next to a VRCStation on the elevator car and tells the lift when the local player enters or leaves it.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandSeat : UdonSharpBehaviour
    {
        public LoopLandLift lift;
        public int index;

        public override void OnStationEntered(VRCPlayerApi player)
        {
            if (lift != null && Utilities.IsValid(player) && player.isLocal) lift._SeatEntered(index);
        }

        public override void OnStationExited(VRCPlayerApi player)
        {
            if (lift != null && Utilities.IsValid(player) && player.isLocal) lift._SeatExited(index);
        }
    }
}
