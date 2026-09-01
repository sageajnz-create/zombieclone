using UnityEngine;

namespace Overrun.Presentation
{
    /// <summary>
    /// Full-screen hint before anyone has joined. Player cameras only exist on rigs, and
    /// rigs only exist after a device presses a button — without this the first screen is
    /// a silent greybox with no instruction.
    /// </summary>
    public sealed class LobbyPrompt : MonoBehaviour
    {
        [SerializeField] private LocalPlayers _localPlayers;
        [SerializeField] private Camera _lobbyCamera;

        private void Awake()
        {
            if (_localPlayers == null) _localPlayers = GetComponent<LocalPlayers>();
        }

        public void Bind(LocalPlayers localPlayers, Camera lobbyCamera)
        {
            _localPlayers = localPlayers;
            _lobbyCamera = lobbyCamera;
        }

        private void OnGUI()
        {
            bool empty = _localPlayers == null || _localPlayers.Count == 0;
            if (!empty) return;
            if (_lobbyCamera != null && !_lobbyCamera.enabled) return;

            const int w = 720;
            const int h = 160;
            var rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.72f, w, h);

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 22,
                wordWrap = true
            };
            style.normal.textColor = Color.white;

            GUI.Label(rect,
                      "OVERRUN\nPress any key or a gamepad face button to join.\n" +
                      "A second device joins as player two (split-screen).",
                      style);
        }
    }
}
