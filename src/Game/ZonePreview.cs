using UnityEngine;

namespace ForestOverlay.Game
{
    public struct PreviewZone
    {
        public Vector3 Center;
        public float Radius;
        public int Kind;      // 0 start, 1 checkpoint, 2 end
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

        private static readonly Color StartColour = new Color(0.30f, 1f, 0.45f, 0.95f);
        private static readonly Color CheckColour = new Color(0.40f, 0.75f, 1f, 0.95f);
        private static readonly Color EndColour = new Color(1f, 0.40f, 0.40f, 0.95f);

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
                if (z.Radius <= 0f) continue;

                GL.Color(z.Kind == 0 ? StartColour : (z.Kind == 2 ? EndColour : CheckColour));
                WireSphere(z.Center, z.Radius);
            }

            GL.End();
            GL.PopMatrix();
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
