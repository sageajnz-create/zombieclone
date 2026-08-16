using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

namespace Overrun.Net
{
    /// <summary>
    /// Brings a VS001 run up: start the host, load the simulation scene through Netcode's
    /// scene manager, and load the local presentation scene plainly.
    ///
    /// The split matters. World is networked — it must go through NetworkManager.SceneManager
    /// so that joining clients receive it and in-scene NetworkObjects resolve consistently.
    /// LocalRigs is presentation and differs per machine, so it must NOT be network-loaded;
    /// pushing it through the network scene manager would try to replicate one machine's
    /// split-screen layout onto everyone else.
    /// </summary>
    public sealed class SessionBootstrapper : MonoBehaviour
    {
        [SerializeField] private bool _autoStartHost = true;
        [SerializeField] private string _worldScene = "World";
        [SerializeField] private string _localRigsScene = "LocalRigs";

        private IEnumerator Start()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogError("[Overrun] No NetworkManager in the Bootstrap scene.", this);
                yield break;
            }

            if (_autoStartHost && !nm.IsListening)
            {
                if (!nm.StartHost())
                {
                    Debug.LogError("[Overrun] StartHost() failed.", this);
                    yield break;
                }
                Debug.Log("[Overrun] Host started.");
            }

            // Let the in-scene NetworkObjects (NetSession) finish spawning before anything
            // tries to locate the session.
            yield return null;

            LoadWorld(nm);
            LoadLocalRigs();
        }

        private void LoadWorld(NetworkManager nm)
        {
            if (SceneManager.GetSceneByName(_worldScene).isLoaded) return;

            bool networked = nm.NetworkConfig != null
                             && nm.NetworkConfig.EnableSceneManagement
                             && nm.SceneManager != null;

            if (networked)
            {
                nm.SceneManager.LoadScene(_worldScene, LoadSceneMode.Additive);
            }
            else
            {
                // Single-machine fallback so the slice still runs with scene management off.
                SceneManager.LoadSceneAsync(_worldScene, LoadSceneMode.Additive);
            }
        }

        private void LoadLocalRigs()
        {
            if (SceneManager.GetSceneByName(_localRigsScene).isLoaded) return;
            SceneManager.LoadSceneAsync(_localRigsScene, LoadSceneMode.Additive);
        }
    }
}
