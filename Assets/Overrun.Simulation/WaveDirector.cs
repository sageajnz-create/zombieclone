using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

namespace Overrun.Simulation
{
    public class WaveDirector : NetworkBehaviour
    {
        public int CurrentRound = 1;
        public float RoundBudget = 10f;
        
        private void Update()
        {
            if (!IsServer) return;
            // Logic for spawning enemies until budget is spent
        }

        public void AdvanceRound()
        {
            CurrentRound++;
            // Recalculate budget based on curve
        }
    }
}
