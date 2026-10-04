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

        public override void ContributeHud(HudBuilder hud)
        {
            PlayerRefView p = new PlayerRefView(Ctx);
            bool compact = hud.Compact;

            // Lines switched off in Settings are not built at all.
            if (hud.Shows("Speed")) hud.Pair("Speed", HudLines.Speed(p.Horizontal, p.Total, compact));

            if (hud.Shows("Vel")) hud.Pair("Vel", HudLines.Vector(p.X, p.Y, p.Z, 1));

            if (hud.Shows("Pos"))
            {
                if (Ctx.Player.Found)
                {
                    Vector3 pos = Ctx.Player.Transform.position;
                    hud.Pair("Pos", HudLines.Vector(pos.x, pos.y, pos.z, 0));
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
                hud.Pair("Lock", "unavailable (" + Ctx.Bridge.LockStatus + ")");
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
