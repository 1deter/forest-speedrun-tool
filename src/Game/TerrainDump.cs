using System;
using System.IO;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The world's terrain written to files once, for the website's map
    // (docs/website.md, Next 4): `call static:ForestOverlay.Game.TerrainDump
    // Write` over the bridge. Dev only - slow (a few seconds), never per
    // frame, no game state touched.
    //
    // Into config/ForestOverlay/terrain/:
    //   terrain.txt  - size, position, resolutions, one line per ground
    //                  texture (name, tile size, average colour)
    //   heights.u16  - heightmapResolution^2 little-endian uint16 (0..65535
    //                  of size.y), row 0 = z min, x east along a row
    //   alpha.u8     - alphamapResolution^2 pixels x layers bytes (0..255
    //                  weight per ground texture), same row order
    // ------------------------------------------------------------------
    public static class TerrainDump
    {
        public static ManualLogSource Log;

        public static string Write()
        {
            Terrain t = Terrain.activeTerrain;
            if (t == null || t.terrainData == null) return "no active terrain";
            TerrainData d = t.terrainData;
            string dir = Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "ForestOverlay"), "terrain");
            Directory.CreateDirectory(dir);

            int hr = d.heightmapResolution;
            using (FileStream f = File.Create(Path.Combine(dir, "heights.u16")))
            {
                byte[] row = new byte[hr * 2];
                const int chunk = 64;
                for (int z0 = 0; z0 < hr; z0 += chunk)
                {
                    int n = Math.Min(chunk, hr - z0);
                    float[,] h = d.GetHeights(0, z0, hr, n);   // [z, x]
                    for (int z = 0; z < n; z++)
                    {
                        for (int x = 0; x < hr; x++)
                        {
                            int v = (int)Math.Round(Mathf.Clamp01(h[z, x]) * 65535f);
                            row[x * 2] = (byte)v;
                            row[x * 2 + 1] = (byte)(v >> 8);
                        }
                        f.Write(row, 0, row.Length);
                    }
                }
            }

            int ar = d.alphamapResolution, layers = d.alphamapLayers;
            if (layers > 0)
            {
                using (FileStream f = File.Create(Path.Combine(dir, "alpha.u8")))
                {
                    byte[] row = new byte[ar * layers];
                    for (int z0 = 0; z0 < ar; z0 += 64)
                    {
                        int n = Math.Min(64, ar - z0);
                        float[,,] a = d.GetAlphamaps(0, z0, ar, n);   // [z, x, layer]
                        for (int z = 0; z < n; z++)
                        {
                            for (int x = 0; x < ar; x++)
                                for (int l = 0; l < layers; l++)
                                    row[x * layers + l] = (byte)Mathf.RoundToInt(Mathf.Clamp01(a[z, x, l]) * 255f);
                            f.Write(row, 0, row.Length);
                        }
                    }
                }
            }

            StringBuilder sb = new StringBuilder();
            Vector3 p = t.transform.position, s = d.size;
            sb.Append("size = ").Append(V(s)).Append('\n');
            sb.Append("position = ").Append(V(p)).Append('\n');
            sb.Append("heightmapResolution = ").Append(hr).Append('\n');
            sb.Append("alphamapResolution = ").Append(ar).Append('\n');
            sb.Append("alphamapLayers = ").Append(layers).Append('\n');
            sb.Append("material = ").Append(t.materialTemplate != null ? t.materialTemplate.name + " / " + t.materialTemplate.shader.name : "(built-in)").Append('\n');
            SplatPrototype[] splats = d.splatPrototypes;
            for (int i = 0; splats != null && i < splats.Length; i++)
            {
                Texture2D tex = splats[i].texture;
                Color c = Average(tex);
                sb.Append("layer ").Append(i).Append(" = ").Append(tex != null ? tex.name : "(none)")
                  .Append(" | tile ").Append(splats[i].tileSize.x.ToString("0.##")).Append(',').Append(splats[i].tileSize.y.ToString("0.##"))
                  .Append(" | rgb ").Append(Mathf.RoundToInt(c.r * 255)).Append(',').Append(Mathf.RoundToInt(c.g * 255)).Append(',').Append(Mathf.RoundToInt(c.b * 255))
                  .Append('\n');
            }
            File.WriteAllText(Path.Combine(dir, "terrain.txt"), sb.ToString());
            string summary = "terrain " + V(s) + " at " + V(p) + ", heights " + hr + "^2, alpha " + ar + "^2 x " + layers + " -> " + dir;
            if (Log != null) Log.LogInfo("Terrain dump: " + summary);
            return summary;
        }

        static string V(Vector3 v)
        {
            return v.x.ToString("0.###") + "," + v.y.ToString("0.###") + "," + v.z.ToString("0.###");
        }

        /// A texture's average colour, read through a small render texture
        /// (the game's textures are not CPU-readable).
        static Color Average(Texture2D tex)
        {
            if (tex == null) return Color.black;
            RenderTexture rt = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture before = RenderTexture.active;
            Texture2D read = new Texture2D(32, 32, TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
                Color[] px = read.GetPixels();
                float r = 0, g = 0, b = 0;
                for (int i = 0; i < px.Length; i++) { r += px[i].r; g += px[i].g; b += px[i].b; }
                return new Color(r / px.Length, g / px.Length, b / px.Length);
            }
            catch (Exception e)
            {
                if (Log != null) Log.LogWarning("Terrain dump: average of " + tex.name + " failed: " + e.Message);
                return Color.black;
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(read);
            }
        }
    }
}
