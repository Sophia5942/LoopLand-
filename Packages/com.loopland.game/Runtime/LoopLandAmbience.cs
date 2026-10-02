using TMPro;
using UdonSharp;
using UnityEngine;

namespace LoopLand
{
    /// <summary>World music with a per-player MUSIC ON/OFF toggle (ambience and water keep playing).</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandAmbience : UdonSharpBehaviour
    {
        public AudioSource music;
        public float musicVolume = 0.3f;
        public TMP_Text[] musicLabels;

        private bool on = true;

        private void Start()
        {
            _Apply();
        }

        public void _ToggleMusic()
        {
            on = !on;
            _Apply();
        }

        private void _Apply()
        {
            if (music != null) music.volume = on ? musicVolume : 0f;
            if (musicLabels == null) return;
            for (int i = 0; i < musicLabels.Length; i++) if (musicLabels[i] != null) musicLabels[i].text = on ? "MUSIC: ON" : "MUSIC: OFF";
        }
    }
}
