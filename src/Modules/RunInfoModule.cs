using ForestOverlay.Core;

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

            hud.Pair("Speed", p.Horizontal.ToString("F2") + " u/s   (tot " +
                              p.Total.ToString("F2") + ")");

            hud.Pair("Vel", p.X.ToString("F1") + ", " +
                            p.Y.ToString("F1") + ", " +
                            p.Z.ToString("F1"));

            if (Ctx.Player.Found)
            {
                hud.Pair("Pos", Ctx.Player.Transform.position.x.ToString("F0") + ", " +
                                Ctx.Player.Transform.position.y.ToString("F0") + ", " +
                                Ctx.Player.Transform.position.z.ToString("F0"));
            }
            else
            {
                hud.Pair("Pos", "player not found yet");
            }
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
