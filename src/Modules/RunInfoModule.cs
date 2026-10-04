using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Velocity and player status. INFO-ONLY - reads, never writes, so it
    // is the kind of thing an autosplitter would also do and is plausibly
    // legal for verified runs.
    //
    // Horizontal speed is listed first because that is the number that
    // matters for movement tech; total magnitude is dominated by fall
    // speed the moment you leave the ground, which hides what the run is
    // actually doing.
    // ------------------------------------------------------------------
    public sealed class RunInfoModule : OverlayModule
    {
        public override string Id { get { return "runinfo"; } }
        public override string DisplayName { get { return "Run info"; } }

        // Each value's text and the numbers it was made from: standing
        // still, nothing is formatted again (ten refreshes a second, eight
        // numbers each). Exact float compares - the same input, the same text.
        private float _speedH = float.NaN, _speedT;
        private bool _speedCompact;
        private string _speedText;
        private Vector3 _velShown = new Vector3(float.NaN, 0f, 0f);
        private string _velText;
        private Vector3 _posShown = new Vector3(float.NaN, 0f, 0f);
        private string _posText;
        private string _lockFor, _lockText;

        public override void ContributeHud(HudBuilder hud)
        {
            PlayerRefView p = new PlayerRefView(Ctx);
            bool compact = hud.Compact;

            // Lines switched off in Settings are not built at all.
            if (hud.Shows("Speed"))
            {
                float h = p.Horizontal, t = p.Total;
                if (_speedText == null || h != _speedH || t != _speedT || compact != _speedCompact)
                {
                    _speedH = h;
                    _speedT = t;
                    _speedCompact = compact;
                    _speedText = HudLines.Speed(h, t, compact);
                }
                hud.Pair("Speed", _speedText);
            }

            if (hud.Shows("Vel"))
            {
                Vector3 v = Ctx.Player.Velocity;
                if (_velText == null || !Same(v, _velShown))
                {
                    _velShown = v;
                    _velText = HudLines.Vector(v.x, v.y, v.z, 1);
                }
                hud.Pair("Vel", _velText);
            }

            if (hud.Shows("Pos"))
            {
                if (Ctx.Player.Found)
                {
                    Vector3 pos = Ctx.Player.Transform.position;
                    if (_posText == null || !Same(pos, _posShown))
                    {
                        _posShown = pos;
                        _posText = HudLines.Vector(pos.x, pos.y, pos.z, 0);
                    }
                    hud.Pair("Pos", _posText);
                }
                else
                {
                    hud.Pair("Pos", compact ? "-" : "player not found yet");
                }
            }

            // Lock state is reported because a failure here used to be
            // invisible - the explorer toggle simply looked dead when the
            // controller had not been bound.
            if (Ctx.Bridge != null && !Ctx.Bridge.PlayerLockAvailable)
            {
                string status = Ctx.Bridge.LockStatus;
                if (_lockText == null || !string.Equals(status, _lockFor))
                {
                    _lockFor = status;
                    _lockText = "unavailable (" + status + ")";
                }
                hud.Pair("Lock", _lockText);
            }
        }

        // Component-wise, not Vector3's == (which is "closer than 1e-5").
        private static bool Same(Vector3 a, Vector3 b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z;
        }

        // Tiny read-only view so the formatting above stays readable.
        private struct PlayerRefView
        {
            private readonly ModuleContext _ctx;
            public PlayerRefView(ModuleContext ctx) { _ctx = ctx; }

            public float Horizontal { get { return _ctx.Player.HorizontalSpeed; } }
            public float Total { get { return _ctx.Player.Speed; } }
            public float X { get { return _ctx.Player.Velocity.x; } }
            public float Y { get { return _ctx.Player.Velocity.y; } }
            public float Z { get { return _ctx.Player.Velocity.z; } }
        }
    }
}
