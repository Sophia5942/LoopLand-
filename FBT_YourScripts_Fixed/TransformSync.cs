using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class TransformSync : UdonSharpBehaviour {
	// Sync
	[UdonSynced] private Vector3 Position;
	[UdonSynced] private Quaternion Rotation;
	// Internal
	private Vector3 LastSyncedPosition;
	private Quaternion LastSyncedRotation;

	private void Update() {
		if(Networking.IsOwner(gameObject)) {
			Position = transform.position;
			Rotation = transform.rotation;
			// FBT FIX: send when it moved 2 cm (was 10 cm, so small moves never reached other players) or turned 1 degree
			bool posChanged = (LastSyncedPosition - Position).sqrMagnitude > 0.0004f;
			bool rotChanged = Quaternion.Angle(LastSyncedRotation, Rotation) > 1f;
			if(posChanged || rotChanged) {
				LastSyncedPosition = Position;
				LastSyncedRotation = Rotation;
				RequestSerialization();
			}
		}
	}
	public override void OnDeserialization() {
		transform.SetPositionAndRotation(Position, Rotation);
	}
}