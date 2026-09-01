using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Overrun.Data;
using Overrun.Net;
using Overrun.Simulation;

namespace Overrun.Presentation
{
    /// <summary>
    /// Per-local-player HUD. Built at runtime so the greybox rig prefab stays empty and
    /// Sage does not have to wire UGUI by hand.
    ///
    /// Reads simulation state; never writes it. Augment picks and restart go through
    /// NetSession RPCs so the host and a future client share the same path.
    /// </summary>
    public sealed class PlayerHud : MonoBehaviour
    {
        private PlayerContext _context;
        private NetSession _session;
        private PlayerPawn _pawn;
        private Canvas _canvas;

        private Text _status;
        private Text _prompt;
        private Text _overlay;
        private Image _crosshair;

        public void Bind(PlayerContext context, NetSession session)
        {
            _context = context;
            _session = session;
            if (context != null) _canvas = context.Hud;
            EnsureUi();
        }

        public void Follow(PlayerPawn pawn) => _pawn = pawn;

        private void Awake()
        {
            if (_context == null) _context = GetComponentInParent<PlayerContext>();
            if (_canvas == null && _context != null) _canvas = _context.Hud;
            EnsureUi();
        }

        private void Update()
        {
            if (_status == null) return;

            PlayerState state = GetState();
            RunContext run = _session != null ? _session.Run : null;

            UpdateCursor(run);
            ReadOfferChoice(state);
            DrawStatus(run, state);
            DrawPrompt(state);
            DrawOverlay(run, state);
        }

        private PlayerState GetState()
        {
            if (_session == null || _context == null) return null;
            _session.TryGetLocalPlayer(_context.LocalSlot, out PlayerState state);
            return state;
        }

        private void DrawStatus(RunContext run, PlayerState state)
        {
            int round = run != null ? run.Round : 0;
            int scrip = state != null ? state.Scrip : 0;

            float hp = 0f, maxHp = 100f;
            if (_pawn != null && _pawn.Health != null)
            {
                hp = _pawn.Health.Current;
                maxHp = _pawn.Health.MaxHealth;
            }
            else if (state != null)
            {
                hp = state.Health;
                maxHp = state.MaxHealth;
            }

            string ammo = "--";
            if (_pawn != null && _pawn.Weapon != null)
            {
                WeaponRuntime w = _pawn.Weapon;
                ammo = w.IsReloading ? "RELOAD" : w.Magazine + " / " + w.Reserve;
            }

            _status.text = "ROUND " + round + "    HP " + Mathf.CeilToInt(hp) + "/" + Mathf.CeilToInt(maxHp) +
                           "    " + ammo + "    SCRIP " + scrip;
        }

        private void DrawPrompt(PlayerState state)
        {
            if (_prompt == null) return;
            if (_pawn == null || state == null || !state.IsAlive)
            {
                _prompt.text = "";
                return;
            }

            if (Physics.Raycast(_pawn.Head.position, _pawn.Head.forward, out RaycastHit hit, 3.2f,
                                ~0, QueryTriggerInteraction.Ignore))
            {
                var interactable = hit.collider.GetComponentInParent<IInteractable>();
                if (interactable != null && interactable.IsAvailable)
                {
                    _prompt.text = interactable.Prompt;
                    return;
                }
            }

            _prompt.text = "";
        }

        private void DrawOverlay(RunContext run, PlayerState state)
        {
            if (_overlay == null) return;
            if (_crosshair != null) _crosshair.enabled = run == null || run.Phase == RunPhase.Playing;

            if (run != null && run.Phase == RunPhase.Ended)
            {
                int round = run.Round;
                int kills = state != null ? state.Kills : 0;
                int scrip = state != null ? state.PeakScrip : 0;
                _overlay.text = "RUN OVER\n\nReached round " + round +
                                "\nKills " + kills + "    Peak scrip " + scrip +
                                "\n\n[Fire] or [Jump] to restart";
                return;
            }

            if (state != null && state.HasPendingOffer)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("AUGMENT\nPick one\n\n");
                for (int i = 0; i < state.PendingOfferCount; i++)
                {
                    AugmentDefinition def = state.GetPendingOffer(i);
                    if (def == null) continue;
                    sb.Append("[").Append(i + 1).Append("]  ").Append(def.DisplayName);
                    if (!string.IsNullOrEmpty(def.Description))
                        sb.Append("  —  ").Append(def.Description);
                    sb.Append("\n");
                }
                sb.Append("\nKeyboard 1/2/3    Gamepad X/Y/B");
                _overlay.text = sb.ToString();
                return;
            }

            _overlay.text = "";
        }

        private void ReadOfferChoice(PlayerState state)
        {
            if (state == null || !state.HasPendingOffer || _session == null || _context == null) return;

            int pick = -1;
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) pick = 0;
                else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) pick = 1;
                else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) pick = 2;
            }

            if (pick < 0 && _context.Input != null)
            {
                foreach (var device in _context.Input.devices)
                {
                    var pad = device as Gamepad;
                    if (pad == null) continue;
                    if (pad.buttonWest.wasPressedThisFrame) pick = 0;
                    else if (pad.buttonNorth.wasPressedThisFrame) pick = 1;
                    else if (pad.buttonEast.wasPressedThisFrame) pick = 2;
                    if (pick >= 0) break;
                }
            }

            if (pick >= 0) _session.RequestAugmentChoiceRpc(_context.LocalSlot, (byte)pick);
        }

        private void UpdateCursor(RunContext run)
        {
            bool ui = run != null && (run.Phase == RunPhase.OfferingAugments || run.Phase == RunPhase.Ended);
            if (ui)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void EnsureUi()
        {
            if (_canvas == null) return;
            if (_status != null) return;

            EnsureEventSystem();

            if (_canvas.GetComponent<CanvasScaler>() == null)
            {
                var scaler = _canvas.gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
            }
            if (_canvas.GetComponent<GraphicRaycaster>() == null)
                _canvas.gameObject.AddComponent<GraphicRaycaster>();

            Font font = LoadFont();
            Transform root = _canvas.transform;

            _status = MakeText(root, "Status", new Vector2(0.02f, 0.88f), new Vector2(0.98f, 0.98f),
                               28, TextAnchor.UpperLeft, font);
            _prompt = MakeText(root, "Prompt", new Vector2(0.2f, 0.18f), new Vector2(0.8f, 0.28f),
                               26, TextAnchor.MiddleCenter, font);
            _overlay = MakeText(root, "Overlay", new Vector2(0.15f, 0.25f), new Vector2(0.85f, 0.85f),
                               30, TextAnchor.MiddleCenter, font);

            _crosshair = MakeImage(root, "Crosshair", new Vector2(0.5f, 0.5f), new Vector2(8f, 8f),
                                   new Color(1f, 1f, 1f, 0.85f));
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        private static Font LoadFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        private static Text MakeText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                     int size, TextAnchor align, Font font)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = align;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = "";
            return text;
        }

        private static Image MakeImage(Transform parent, string name, Vector2 anchor, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
    }
}
