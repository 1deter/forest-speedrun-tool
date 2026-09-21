using System;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Finds and tracks the local player.
    //
    // Extracted out of Plugin so that every module reads the same
    // resolved references instead of each one running its own scene
    // search. The search is retried on a timer because the player does
    // not exist yet while the main menu is up.
    //
    // Confirmed layout: root GameObject is "player", tagged Player, with
    // FirstPersonCharacter and a Rigidbody on it. Velocity is read from
    // the Rigidbody - that is the authoritative value the game itself
    // integrates.
    // ------------------------------------------------------------------
    public sealed class PlayerRef
    {
        private readonly ManualLogSource _log;

        public Transform Transform;
        public Rigidbody Rigidbody;
        public CharacterController Controller;
        public string Source = "searching...";

        private Vector3 _lastPosition;
        private float _nextSearchTime;

        public bool Found { get { return Transform != null; } }
        public Vector3 Velocity { get; private set; }
        public float Speed { get { return Velocity.magnitude; } }

        /// Horizontal speed. This is the number that actually matters for
        /// movement tech - vertical velocity from falling otherwise
        /// dominates the magnitude and hides what the run is doing.
        public float HorizontalSpeed
        {
            get { return new Vector2(Velocity.x, Velocity.z).magnitude; }
        }

        public PlayerRef(ManualLogSource log)
        {
            _log = log;
        }

        public void Tick()
        {
            Acquire();
            UpdateVelocity();
        }

        private void Acquire()
        {
            if (Transform != null) return;
            if (Time.unscaledTime < _nextSearchTime) return;
            _nextSearchTime = Time.unscaledTime + 0.5f;

            GameObject found = null;
            string source = null;

            try
            {
                found = GameObject.FindGameObjectWithTag("Player");
                if (found != null) source = "tag:Player";
            }
            catch (Exception) { }

            if (found == null && Camera.main != null)
            {
                found = Camera.main.transform.root.gameObject;
                source = "camRoot:" + found.name;
            }

            if (found == null)
            {
                CharacterController cc =
                    UnityEngine.Object.FindObjectOfType(typeof(CharacterController)) as CharacterController;
                if (cc != null)
                {
                    found = cc.gameObject;
                    source = "charCtrl:" + found.name;
                }
            }

            if (found == null) return;

            Transform = found.transform;
            Rigidbody = found.GetComponentInChildren<Rigidbody>();
            Controller = found.GetComponentInChildren<CharacterController>();
            _lastPosition = Transform.position;
            Source = source;

            _log.LogInfo("Player acquired via " + source);
        }

        private void UpdateVelocity()
        {
            if (Transform == null)
            {
                Velocity = Vector3.zero;
                return;
            }

            if (Rigidbody != null) Velocity = Rigidbody.velocity;
            else if (Controller != null) Velocity = Controller.velocity;
            else
            {
                if (Time.deltaTime > 0f)
                    Velocity = (Transform.position - _lastPosition) / Time.deltaTime;
                _lastPosition = Transform.position;
            }
        }

        // ------------------------------------------------------------------
        // Teleport. STATE-ALTERING - practice only, never on a real run.
        //
        // Zeroing velocity matters: arriving at a saved spot with the old
        // momentum still applied throws you straight back off it, and
        // keeping fall speed can kill you on landing.
        // ------------------------------------------------------------------
        public bool MoveTo(Vector3 position, Quaternion rotation)
        {
            if (Transform == null) return false;

            if (Rigidbody != null)
            {
                Rigidbody.velocity = Vector3.zero;
                Rigidbody.angularVelocity = Vector3.zero;
                Rigidbody.position = position;
                Rigidbody.rotation = rotation;
                Transform.position = position;
                Transform.rotation = rotation;
            }
            else if (Controller != null)
            {
                // The controller overwrites transform writes while enabled.
                Controller.enabled = false;
                Transform.position = position;
                Transform.rotation = rotation;
                Controller.enabled = true;
            }
            else
            {
                Transform.position = position;
                Transform.rotation = rotation;
            }

            _lastPosition = position;
            return true;
        }
    }
}
