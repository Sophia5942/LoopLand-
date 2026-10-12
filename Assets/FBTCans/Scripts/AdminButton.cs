using UdonSharp;
using UnityEngine;

/// <summary>A button on the drinks admin panel. Action 0 = lock / unlock, 1 = reset all drinks, 2 = close the panel.</summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AdminButton : UdonSharpBehaviour {
	public DrinksAdmin Panel;
	public int Action;
	[Tooltip("The part that moves in when pressed.")]
	public Transform Cap;
	private Vector3 CapRest;
	private bool HaveRest;

	public override void Interact() {
		if(Panel == null) return;
		if(Action == 0) Panel._ToggleLock();
		else if(Action == 1) Panel._ResetAll();
		else { Panel._ClosePanel(); return; }
		if(Cap != null) {
			if(!HaveRest) { CapRest = Cap.localPosition; HaveRest = true; }
			Cap.localPosition = CapRest - new Vector3(0f, 0f, 0.006f);   // pushed in (the front faces +Z)
			SendCustomEventDelayedSeconds(nameof(_Release), 0.15f);
		}
	}
	public void _Release() { if(Cap != null && HaveRest) Cap.localPosition = CapRest; }
}
