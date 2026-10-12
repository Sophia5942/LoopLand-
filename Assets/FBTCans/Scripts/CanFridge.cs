using UdonSharp;
using UnityEngine;

/// <summary>
/// "Get a new can": Interact with the fridge and a can appears on top of it. Uses a free can from the pool; if all
/// are out, it reuses the oldest one nobody is holding. Optional cooldown per player.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CanFridge : UdonSharpBehaviour {
	public FBTCan[] Cans;
	public Transform SpawnPoint;
	[Tooltip("Seconds before the same player can take another can. 0 = no cooldown.")]
	public float CooldownSeconds = 5f;
	// Internal
	private float NextAllowed;
	private int Spread;

	public override void Interact() {
		if(Time.time < NextAllowed || Cans == null || Cans.Length == 0) return;
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
		// spread spawned cans a little so they don't land inside each other
		Spread = (Spread + 1) % 3;
		Vector3 pos = SpawnPoint.position + SpawnPoint.right * ((Spread - 1) * 0.09f);
		can.Spawn(pos, SpawnPoint.rotation);
		NextAllowed = Time.time + CooldownSeconds;
	}
}
