using UdonSharp;
using UnityEngine;

/// <summary>
/// "Get A New Can": an invisible box inside your fridge. Click it and a can appears in front of it, standing on the
/// shelf, ready to grab. Uses a free can from the pool; if all are out, it reuses the oldest one nobody is holding.
/// Cooldown per player. No sync of its own: the can syncs itself.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CanFridge : UdonSharpBehaviour {
	public FBTCan[] Cans;
	[Tooltip("Where the new can appears (in front of the click box, on the shelf).")]
	public Transform SpawnPoint;
	[Tooltip("Seconds before the same player can get another can. 0 = no cooldown.")]
	public float CooldownSeconds = 600f;
	// Internal
	private float NextAllowed;
	private int Spread;

	public override void Interact() {
		if(Time.time < NextAllowed || Cans == null || Cans.Length == 0 || SpawnPoint == null) return;
		FBTCan can = null;
		for(int i = 0; i < Cans.Length; i++) {
			if(Cans[i] != null && !Cans[i].IsActive()) { can = Cans[i]; break; }
		}
		if(can == null) {
			// all out: reuse the oldest one nobody is holding
			float oldest = float.MaxValue;
			for(int i = 0; i < Cans.Length; i++) {
				if(Cans[i] == null || Cans[i].IsHeld()) continue;
				if(Cans[i].SpawnTime < oldest) { oldest = Cans[i].SpawnTime; can = Cans[i]; }
			}
		}
		if(can == null) return;
		// spread cans along the shelf so they don't appear inside each other
		Spread = (Spread + 1) % 3;
		Vector3 pos = SpawnPoint.position + SpawnPoint.right * ((Spread - 1) * 0.08f);
		can.Spawn(pos, SpawnPoint.rotation);
		NextAllowed = Time.time + CooldownSeconds;
	}
}
