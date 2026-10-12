using UdonSharp;
using UnityEngine;

/// <summary>Trigger inside the bin: a can dropped in is cleaned up (back to the fridge's pool), with a clank.</summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class TrashBin : UdonSharpBehaviour {
	private void OnTriggerEnter(Collider other) { Check(other); }
	private void OnTriggerStay(Collider other) { Check(other); }
	private void Check(Collider other) {
		if(other == null) return;
		FBTCan can = other.GetComponent<FBTCan>();
		if(can != null) can._Trash();
	}
}
