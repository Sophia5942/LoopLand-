using UdonSharp;
using UnityEngine;

namespace LoopLand
{
    /// <summary>Physical 3D button: Interact sends an event (plus an int argument) to a target behaviour.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandButton : UdonSharpBehaviour
    {
        public UdonSharpBehaviour target;
        public string eventName;
        public int arg;
        public Transform pressVisual;

        private Vector3 restPos;
        private bool hasRest;

        public override void Interact()
        {
            if (target == null) return;
            target.SetProgramVariable("pressedArg", arg);
            target.SendCustomEvent(eventName);
            if (pressVisual == null) return;
            if (!hasRest) { restPos = pressVisual.localPosition; hasRest = true; }
            pressVisual.localPosition = restPos + new Vector3(0f, 0f, 0.012f);
            SendCustomEventDelayedSeconds(nameof(_Release), 0.12f);
        }

        public void _Release()
        {
            if (pressVisual != null && hasRest) pressVisual.localPosition = restPos;
        }
    }
}
