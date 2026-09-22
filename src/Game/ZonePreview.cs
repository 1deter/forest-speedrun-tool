using UnityEngine;

namespace ForestOverlay.Game
{
    public struct PreviewZone
    {
        public Vector3 Center;
        public float Radius;      // sphere
        public Vector3 Extents;   // box half-size
        public bool IsBox;
        public int Kind;          // 0 start, 1 checkpoint, 2 end
    }

    // ------------------------------------------------------------------
    // Draws the zones being edited, in the world.
    //
    // Typing a radius and hoping is guesswork - a 3m zone and a 12m zone
    // are indistinguishable on a number field but completely different to
    // run into. The editor pushes whatever it is editing here: start
    // green, checkpoints blue, end red.
    //
    // Wire spheres rather than boxes, because a zone IS a sphere and
    // drawing it as a box would misrepresent where it actually fires.
    // Three orthogonal rings read as a sphere from any angle without the
    // cost of a real mesh.
    // ------------------------------------------------------------------
    public sealed class ZonePreviewBehaviour : MonoBehaviour
    {
        private const int Segments = 28;

        public bool Show;
        public PreviewZone[] Zones;
        public int Count;

        private Material _material;

        // Traffic-light hues, deliberately DESATURATED.
        //
        // The first version used near-full-saturation neon, which is
        // hard to tell apart on an OLED: at that intensity the display
        // is pushing primaries close to their limits and adjacent hues
        // stop separating. Pulling saturation down and spreading the
        // hues (green / amber / red rather than green / blue / red)
        // keeps them distinguishable, and green-amber-red already reads
        // as start-middle-end without a legend.
        private static readonly Color StartColour = new Color(0.38f, 0.78f, 0.45f, 0.85f);
        private static readonly Color CheckColour = new Color(0.95f, 0.72f, 0.26f, 0.85f);
        private static readonly Color EndColour = new Color(0.88f, 0.34f, 0.34f, 0.85f);

        private void EnsureMaterial()
        {
            if (_material != null) return;

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return;

            _material = new Material(shader);
            _material.hideFlags = HideFlags.HideAndDontSave;
            _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _material.SetInt("_ZWrite", 0);
        }

        private void OnRenderObject()
        {
            if (!Show || Zones == null || Count <= 0) return;

            EnsureMaterial();
            if (_material == null) return;

            _material.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);

            int n = Mathf.Min(Count, Zones.Length);
            for (int i = 0; i < n; i++)
            {
                PreviewZone z = Zones[i];
                if (!z.IsBox && z.Radius <= 0f) continue;

                GL.Color(z.Kind == 0 ? StartColour : (z.Kind == 2 ? EndColour : CheckColour));

                if (z.IsBox) WireBox(z.Center, z.Extents);
                else WireSphere(z.Center, z.Radius);

                // A post through the centre makes a zone findable when
                // you are outside it and the outline is edge-on.
                float height = z.IsBox ? z.Extents.y : z.Radius;
                GL.Vertex(new Vector3(z.Center.x, z.Center.y - height, z.Center.z));
                GL.Vertex(new Vector3(z.Center.x, z.Center.y + height, z.Center.z));
            }

            GL.End();
            GL.PopMatrix();
        }

        private static void WireBox(Vector3 c, Vector3 e)
        {
            Vector3 p000 = new Vector3(c.x - e.x, c.y - e.y, c.z - e.z);
            Vector3 p001 = new Vector3(c.x - e.x, c.y - e.y, c.z + e.z);
            Vector3 p010 = new Vector3(c.x - e.x, c.y + e.y, c.z - e.z);
            Vector3 p011 = new Vector3(c.x - e.x, c.y + e.y, c.z + e.z);
            Vector3 p100 = new Vector3(c.x + e.x, c.y - e.y, c.z - e.z);
            Vector3 p101 = new Vector3(c.x + e.x, c.y - e.y, c.z + e.z);
            Vector3 p110 = new Vector3(c.x + e.x, c.y + e.y, c.z - e.z);
            Vector3 p111 = new Vector3(c.x + e.x, c.y + e.y, c.z + e.z);

            Edge(p000, p001); Edge(p001, p011); Edge(p011, p010); Edge(p010, p000);
            Edge(p100, p101); Edge(p101, p111); Edge(p111, p110); Edge(p110, p100);
            Edge(p000, p100); Edge(p001, p101); Edge(p011, p111); Edge(p010, p110);
        }

        private static void Edge(Vector3 a, Vector3 b)
        {
            GL.Vertex(a);
            GL.Vertex(b);
        }

        private static void WireSphere(Vector3 c, float r)
        {
            Ring(c, r, 0);
            Ring(c, r, 1);
            Ring(c, r, 2);
        }

        private static void Ring(Vector3 c, float r, int axis)
        {
            Vector3 previous = PointOn(c, r, axis, 0f);

            for (int i = 1; i <= Segments; i++)
            {
                float a = (i / (float)Segments) * Mathf.PI * 2f;
                Vector3 p = PointOn(c, r, axis, a);

                GL.Vertex(previous);
                GL.Vertex(p);
                previous = p;
            }
        }

        private static Vector3 PointOn(Vector3 c, float r, int axis, float a)
        {
            float s = Mathf.Sin(a) * r;
            float t = Mathf.Cos(a) * r;

            if (axis == 0) return new Vector3(c.x + t, c.y + s, c.z);
            if (axis == 1) return new Vector3(c.x + t, c.y, c.z + s);
            return new Vector3(c.x, c.y + t, c.z + s);
        }

        private void OnDestroy()
        {
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}
