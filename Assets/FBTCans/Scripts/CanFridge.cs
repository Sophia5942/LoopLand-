using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// "Get A New Can": an invisible box inside your fridge. Click it and a can appears in front of it, standing on the
/// shelf, ready to grab. Uses a free can from the pool; if all are out, it reuses the oldest one nobody is holding.
/// Anti-spam: there are only ever as many cans as the pool (8), every player has a cooldown, and the cans remember
/// who spawned recently so a hacked client that skips the cooldown is refused by everyone else (see FBTCan).
/// No sync of its own: the can syncs itself.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CanFridge : UdonSharpBehaviour {
	public FBTCan[] Cans;
	[Tooltip("Where the new can appears (in front of the click box, on the shelf).")]
	public Transform SpawnPoint;
	[Tooltip("Seconds before the same player can get another can.")]
	public float CooldownSeconds = 10f;
	[Tooltip("Optional admin panel that can lock this row and clear its cans.")]
	public DrinksAdmin Admin;
	// Internal: who spawned a can recently (filled from what everyone sees, not just this player)
	private const int Slots = 32;
	private int[] RecentIds;
	private float[] RecentTimes;
	private int RecentNext;
	private int Spread;
	private Collider ClickBox;

	private void Start() {
		InitRecent();
		ClickBox = GetComponent<Collider>();
	}

	public override void Interact() {
		VRCPlayerApi me = Networking.LocalPlayer;
		if(me == null || Cans == null || Cans.Length == 0 || SpawnPoint == null) return;
		if(Admin != null && Admin.IsLocked()) return;
		if(_SpawnedWithin(me.playerId, CooldownSeconds)) return;
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
	}

	// Admin panel
	public void _SetLocked(bool locked) {
		// locked: the row can't be clicked at all
		if(ClickBox == null) ClickBox = GetComponent<Collider>();
		if(ClickBox != null) ClickBox.enabled = !locked;
	}
	public void _AdminClear() {
		if(Cans == null) return;
		for(int i = 0; i < Cans.Length; i++) if(Cans[i] != null) Cans[i]._AdminClear();
	}
	public bool _BlocksSpawnBy(VRCPlayerApi p) {
		// while locked, only admins may take a free can
		return Admin != null && Admin.IsLocked() && !Admin._IsAdmin(p);
	}

	// Called by the cans whenever anyone spawns one (locally or seen over the network)
	public void _NoteSpawn(int playerId) {
		InitRecent();
		for(int i = 0; i < Slots; i++) {
			if(RecentIds[i] == playerId) { RecentTimes[i] = Time.time; return; }
		}
		RecentIds[RecentNext] = playerId;
		RecentTimes[RecentNext] = Time.time;
		RecentNext = (RecentNext + 1) % Slots;
	}
	public bool _SpawnedWithin(int playerId, float seconds) {
		InitRecent();
		for(int i = 0; i < Slots; i++) {
			if(RecentIds[i] == playerId) return Time.time - RecentTimes[i] < seconds;
		}
		return false;
	}
	private void InitRecent() {
		if(RecentIds != null) return;
		RecentIds = new int[Slots];
		RecentTimes = new float[Slots];
		for(int i = 0; i < Slots; i++) { RecentIds[i] = -1; RecentTimes[i] = -9999f; }
	}
}
