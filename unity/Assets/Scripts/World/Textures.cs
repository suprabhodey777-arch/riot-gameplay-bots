// Procedural textures, generated once and cached: tileable value-noise fbm for
// concrete, painted metal panels, a tiled floor with grime, crate planks and a
// ribbed trim, each with a normal map derived from its height; plus small FX
// textures (bullet hole, soft dot, flash star, tracer, hazard stripes, the
// plant-zone stencil and the scope mask). Greyscale albedo so the palette and
// material colours tint them.
using UnityEngine;

namespace Saltworks
{
    public static class Textures
    {
        // ---- tileable noise ----------------------------------------------------------------------
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777215f;
            }
        }

        static int Wrap(int v, int p)
        {
            int m = v % p;
            return m < 0 ? m + p : m;
        }

        /// <summary>Value noise on a lattice of `period` cells over u, v in [0, 1): tiles exactly.</summary>
        static float Value(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int xa = Wrap(x0, period), xb = Wrap(x0 + 1, period), ya = Wrap(y0, period), yb = Wrap(y0 + 1, period);
            float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Fractal sum of `octaves` value-noise layers, 0..1, tileable.</summary>
        public static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int p = period;
            for (int o = 0; o < octaves; o++)
            {
                sum += Value(u, v, p, seed + o * 31) * amp;
                norm += amp;
                amp *= 0.5f;
                p *= 2;
            }
            return sum / Mathf.Max(1e-5f, norm);
        }

        // ---- helpers -----------------------------------------------------------------------------
        static Texture2D New(string name, int w, int h, bool mips, bool linear, TextureWrapMode wrap)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, mips, linear)
            {
                name = name,
                wrapMode = wrap,
                filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear,
                anisoLevel = mips ? 8 : 1,
                hideFlags = HideFlags.DontSave,
            };
        }

        static Texture2D Finish(Texture2D t, Color32[] px)
        {
            t.SetPixels32(px);
            t.Apply(t.mipmapCount > 1, false);
            return t;
        }

        static byte B(float v)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        }

        /// <summary>GLSL smoothstep: 0 below e0, 1 above e1, smooth between (Unity's Mathf.SmoothStep is an interpolation).</summary>
        public static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / Mathf.Max(1e-5f, e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>A tangent-space normal map from a tileable height field (RGB normal, A 1; linear).</summary>
        static Texture2D NormalMap(string name, float[] h, int n, float strength)
        {
            Texture2D t = New(name, n, n, true, true, TextureWrapMode.Repeat);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = h[y * n + Wrap(x + 1, n)] - h[y * n + Wrap(x - 1, n)];
                    float dy = h[Wrap(y + 1, n) * n + x] - h[Wrap(y - 1, n) * n + x];
                    var nn = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                    px[y * n + x] = new Color32(B(nn.x * 0.5f + 0.5f), B(nn.y * 0.5f + 0.5f), B(nn.z * 0.5f + 0.5f), 255);
                }
            }
            return Finish(t, px);
        }

        public struct Pair
        {
            public Texture2D Albedo;
            public Texture2D Normal;
        }

        static Pair concrete, metal, tiles, crate, trim, ground, brick, flags, deck, timber;
        static Texture2D grain, hole, dot, star, tracer, hazard, plant, scope, glowStrip;

        // ---- surfaces ----------------------------------------------------------------------------
        /// <summary>Cast concrete, one repeat = one 2 m panel: fine noise, pores, form-tie holes, soft stains. A = smoothness.</summary>
        public static Pair Concrete()
        {
            if (concrete.Albedo != null) return concrete;
            const int n = 256;
            var px = new Color32[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    float fine = Fbm(u, v, 16, 4, 11);
                    float broad = Fbm(u, v, 4, 3, 12);
                    float stain = Fbm(u * 1f, v, 2, 3, 13);
                    float c = 0.80f + (fine - 0.5f) * 0.12f + (broad - 0.5f) * 0.10f - Mathf.Max(0f, stain - 0.58f) * 0.35f;
                    float height = fine * 0.6f + broad * 0.4f;
                    // Pores.
                    if (Hash(x, y, 77) < 0.010f)
                    {
                        c -= 0.16f;
                        height -= 0.5f;
                    }
                    // Form-tie holes on a 1 m grid inside the panel.
                    for (int k = 0; k < 4; k++)
                    {
                        float hx = (k % 2 == 0 ? 0.25f : 0.75f) * n, hy = (k < 2 ? 0.25f : 0.75f) * n;
                        float d = new Vector2(x - hx, y - hy).magnitude;
                        if (d < 4.5f)
                        {
                            float t = 1f - d / 4.5f;
                            c -= 0.28f * t;
                            height -= 1.2f * t;
                        }
                    }
                    // Panel seams.
                    if (x < 2 || y < 2)
                    {
                        c *= 0.78f;
                        height -= 1.5f;
                    }
                    float smooth = 0.28f + (1f - fine) * 0.25f;
                    px[y * n + x] = new Color32(B(c), B(c * 0.995f), B(c * 0.985f), B(smooth));
                    h[y * n + x] = height;
                }
            }
            concrete.Albedo = Finish(New("SaltworksConcrete", n, n, true, false, TextureWrapMode.Repeat), px);
            concrete.Normal = NormalMap("SaltworksConcreteN", h, n, 3f);
            return concrete;
        }

        /// <summary>Painted metal panels (2 x 2 per repeat): recessed seams, rivets, chipped paint near seams, drip streaks.</summary>
        public static Pair PaintedMetal()
        {
            if (metal.Albedo != null) return metal;
            const int n = 256;
            var px = new Color32[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    int lx = x % 128, ly = y % 128;
                    float edge = Mathf.Min(Mathf.Min(lx, 127 - lx), Mathf.Min(ly, 127 - ly));
                    float fine = Fbm(u, v, 32, 3, 21);
                    float chipN = Fbm(u, v, 16, 4, 22);
                    float c = 0.88f + (fine - 0.5f) * 0.05f;
                    float height = 0f;
                    float smooth = 0.55f;
                    // Chipped paint where the noise is high close to a seam: darker bare metal.
                    float chip = chipN - Mathf.Clamp01(edge / 26f) * 0.35f;
                    if (chip > 0.6f)
                    {
                        c = 0.55f + fine * 0.1f;
                        height -= 0.4f;
                        smooth = 0.75f;
                    }
                    // Drip streaks running down from the top of each panel.
                    float streak = Value(u, 0f, 64, 23);
                    float streakFade = Mathf.Clamp01(1f - ly / 128f);
                    if (streak > 0.72f) c -= (streak - 0.72f) * 0.5f * streakFade * (0.6f + 0.4f * Fbm(u, v * 0.25f, 8, 2, 24));
                    // Seams.
                    if (edge < 2f)
                    {
                        c *= 0.6f;
                        height -= 1.6f;
                        smooth = 0.2f;
                    }
                    // Rivets along the seams.
                    if (edge >= 4f && edge < 9f)
                    {
                        int along = (lx < 9 || lx > 118) ? ly : lx;
                        float rd = new Vector2((along % 16) - 8f, edge - 6.5f).magnitude;
                        if (rd < 2.3f)
                        {
                            c += 0.08f;
                            height += 1.2f * (1f - rd / 2.3f);
                            smooth = 0.7f;
                        }
                    }
                    px[y * n + x] = new Color32(B(c), B(c), B(c), B(smooth));
                    h[y * n + x] = height + fine * 0.15f;
                }
            }
            metal.Albedo = Finish(New("SaltworksMetal", n, n, true, false, TextureWrapMode.Repeat), px);
            metal.Normal = NormalMap("SaltworksMetalN", h, n, 2.2f);
            return metal;
        }

        /// <summary>The floor: 4 x 4 one-metre tiles per repeat (one repeat = 4 m), dark grout, per-tile tone, grime.</summary>
        public static Pair FloorTiles()
        {
            if (tiles.Albedo != null) return tiles;
            const int n = 256;
            const int tile = n / 4;
            var px = new Color32[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    int tx = x / tile, ty = y / tile;
                    int lx = x % tile, ly = y % tile;
                    float edge = Mathf.Min(Mathf.Min(lx, tile - 1 - lx), Mathf.Min(ly, tile - 1 - ly));
                    float tone = 0.86f + (Hash(tx, ty, 31) - 0.5f) * 0.09f;
                    float fine = Fbm(u, v, 32, 3, 32);
                    float grime = Fbm(u, v, 4, 4, 33);
                    float c = tone + (fine - 0.5f) * 0.06f;
                    c *= Mathf.Lerp(0.82f, 1.02f, Smooth(0.25f, 0.75f, grime));
                    float height = fine * 0.3f;
                    float smooth = 0.42f * Mathf.Lerp(0.5f, 1f, grime);
                    // A bevel into the grout: darker near the joint.
                    if (edge < 6f) c *= Mathf.Lerp(0.88f, 1f, edge / 6f);
                    if (edge < 2f)
                    {
                        c = 0.45f + fine * 0.08f;
                        height = -1.5f;
                        smooth = 0.08f;
                    }
                    else height += Mathf.Clamp01(edge / 4f) * 0.6f;
                    px[y * n + x] = new Color32(B(c), B(c * 0.99f), B(c * 0.97f), B(smooth));
                    h[y * n + x] = height;
                }
            }
            tiles.Albedo = Finish(New("SaltworksFloorTiles", n, n, true, false, TextureWrapMode.Repeat), px);
            tiles.Normal = NormalMap("SaltworksFloorTilesN", h, n, 2.5f);
            return tiles;
        }

        /// <summary>Coarse concrete for the ground outside the walls.</summary>
        public static Pair Ground()
        {
            if (ground.Albedo != null) return ground;
            const int n = 128;
            var px = new Color32[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    float f = Fbm(u, v, 8, 4, 41);
                    float c = 0.78f + (f - 0.5f) * 0.18f;
                    px[y * n + x] = new Color32(B(c), B(c), B(c * 0.98f), B(0.15f));
                    h[y * n + x] = f;
                }
            }
            ground.Albedo = Finish(New("SaltworksGround", n, n, true, false, TextureWrapMode.Repeat), px);
            ground.Normal = NormalMap("SaltworksGroundN", h, n, 2f);
            return ground;
        }

        /// <summary>Crate planks: eight horizontal boards per repeat with grain and dark gaps.</summary>
        public static Pair Crate()
        {
            if (crate.Albedo != null) return crate;
            const int n = 256;
            const int board = n / 8;
            var px = new Color32[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                int b = y / board;
                int ly = y % board;
                float tone = 0.86f + (Hash(b, 0, 51) - 0.5f) * 0.12f;
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    float grainN = Fbm(u * 1f, v, 4, 2, 52 + b) * 0.5f + Value(u, v, 64, 53) * 0.2f + Mathf.Sin((v * 8f + Fbm(u, v, 8, 2, 54) * 0.6f) * 40f) * 0.3f;
                    float c = tone + (grainN - 0.3f) * 0.08f;
                    float height = grainN * 0.3f;
                    if (ly < 2)
                    {
                        c *= 0.45f;
                        height = -1.4f;
                    }
                    else if (ly < 4) c *= 0.85f;
                    // Nail heads near the board ends.
                    int lx = x % 128;
                    if ((lx == 10 || lx == 118) && ly > board / 2 - 3 && ly < board / 2 + 1) c *= 0.55f;
                    px[y * n + x] = new Color32(B(c), B(c * 0.97f), B(c * 0.92f), B(0.22f));
                    h[y * n + x] = height;
                }
            }
            crate.Albedo = Finish(New("SaltworksCrate", n, n, true, false, TextureWrapMode.Repeat), px);
            crate.Normal = NormalMap("SaltworksCrateN", h, n, 2.5f);
            return crate;
        }

        /// <summary>A ribbed dark trim for wall bases and metal fittings.</summary>
        public static Pair Trim()
        {
            if (trim.Albedo != null) return trim;
            const int n = 128;
            var px = new Color32[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    float rib = Mathf.Sin(v * Mathf.PI * 2f * 6f) * 0.5f + 0.5f;
                    float f = Fbm(u, v, 16, 3, 61);
                    float c = 0.62f + rib * 0.12f + (f - 0.5f) * 0.1f;
                    px[y * n + x] = new Color32(B(c), B(c), B(c * 1.02f), B(0.5f + f * 0.2f));
                    h[y * n + x] = rib * 0.8f + f * 0.2f;
                }
            }
            trim.Albedo = Finish(New("SaltworksTrim", n, n, true, false, TextureWrapMode.Repeat), px);
            trim.Normal = NormalMap("SaltworksTrimN", h, n, 2f);
            return trim;
        }

        // ---- round 6 surfaces ------------------------------------------------------------------------
        /// <summary>
        /// Weathered brick, running bond, 8 bricks across and 16 courses per repeat (one repeat = one 2 m wall panel):
        /// per-brick tone, chipped arrises, recessed mortar, soot and damp towards the foot. A = smoothness.
        /// </summary>
        public static Pair Brick()
        {
            if (brick.Albedo != null) return brick;
            const int n = 256;
            const int courses = 16, across = 8;
            const int ch = n / courses, bw = n / across;
            var px = new Color32[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                int row = y / ch;
                int ly = y % ch;
                int shift = (row % 2) * (bw / 2);
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    int xs = Wrap(x + shift, n);
                    int col = xs / bw;
                    int lx = xs % bw;
                    float edge = Mathf.Min(Mathf.Min(lx, bw - 1 - lx), Mathf.Min(ly, ch - 1 - ly));
                    float fine = Fbm(u, v, 32, 3, 101);
                    float chipN = Fbm(u, v, 16, 3, 102);
                    float tone = 0.78f + (Hash(col, row, 103) - 0.5f) * 0.22f;
                    float c = tone + (fine - 0.5f) * 0.12f;
                    float height = 0.6f + fine * 0.3f;
                    float smooth = 0.18f;
                    // Chipped edges: the mortar line eats into the brick where the noise is high.
                    float mortarW = 1.6f + Mathf.Max(0f, chipN - 0.55f) * 6f;
                    if (edge < mortarW)
                    {
                        c = 0.62f + fine * 0.1f;
                        height = -0.6f;
                        smooth = 0.08f;
                    }
                    else if (edge < mortarW + 1.5f) c *= 0.9f;
                    // Damp and soot: darker towards the bottom of the repeat, in soft patches.
                    float damp = Fbm(u, v * 0.5f, 4, 3, 104);
                    c *= Mathf.Lerp(1f, 0.82f, Smooth(0.5f, 0.8f, damp) * (1f - v));
                    px[y * n + x] = new Color32(B(c), B(c * 0.97f), B(c * 0.95f), B(smooth));
                    h[y * n + x] = height;
                }
            }
            brick.Albedo = Finish(New("SaltworksBrick", n, n, true, false, TextureWrapMode.Repeat), px);
            brick.Normal = NormalMap("SaltworksBrickN", h, n, 3f);
            return brick;
        }

        /// <summary>
        /// Stone flagstones, one repeat = 4 m: four courses of stones of random length, each its own tone, worn
        /// smooth in the middle, with dark recessed seams and a little moss-like grime in them. A = smoothness.
        /// </summary>
        public static Pair Flagstones()
        {
            if (flags.Albedo != null) return flags;
            const int n = 256;
            const int rows = 4;
            const int rh = n / rows;
            var px = new Color32[n * n];
            var h = new float[n * n];
            // Per row: cut points along u (tileable: the row wraps), 3 to 5 stones.
            var cuts = new int[rows][];
            for (int r = 0; r < rows; r++)
            {
                int count = 3 + (int)(Hash(r, 0, 111) * 2.99f);
                cuts[r] = new int[count];
                float off = Hash(r, 1, 112) * n;
                for (int i = 0; i < count; i++)
                {
                    float jitter = (Hash(r, i + 2, 113) - 0.5f) * 0.45f;
                    cuts[r][i] = Wrap(Mathf.RoundToInt(off + (i + jitter) * n / (float)count), n);
                }
                System.Array.Sort(cuts[r]);
            }
            for (int y = 0; y < n; y++)
            {
                int row = y / rh;
                int ly = y % rh;
                int[] cr = cuts[row];
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    // Which stone, and the distance to its side seams.
                    int stone = 0;
                    float dSide = float.MaxValue;
                    for (int i = 0; i < cr.Length; i++)
                    {
                        int d = Mathf.Abs(x - cr[i]);
                        d = Mathf.Min(d, n - d);
                        dSide = Mathf.Min(dSide, d);
                        if (x >= cr[i]) stone = i + 1;
                    }
                    if (stone >= cr.Length) stone = 0;
                    float edge = Mathf.Min(dSide, Mathf.Min(ly, rh - 1 - ly));
                    float fine = Fbm(u, v, 32, 4, 114);
                    float broad = Fbm(u, v, 4, 3, 115);
                    float tone = 0.8f + (Hash(stone, row, 116) - 0.5f) * 0.16f;
                    float c = tone + (fine - 0.5f) * 0.1f + (broad - 0.5f) * 0.08f;
                    float height = fine * 0.4f + Mathf.Clamp01(edge / 8f) * 0.6f;
                    float smooth = 0.25f + Mathf.Clamp01(edge / 20f) * 0.2f;
                    if (edge < 6f) c *= Mathf.Lerp(0.82f, 1f, edge / 6f);
                    if (edge < 2f)
                    {
                        float grime = Fbm(u, v, 16, 2, 117);
                        c = 0.36f + grime * 0.1f;
                        height = -1.2f;
                        smooth = 0.05f;
                    }
                    px[y * n + x] = new Color32(B(c), B(c * 0.985f), B(c * 0.955f), B(smooth));
                    h[y * n + x] = height;
                }
            }
            flags.Albedo = Finish(New("SaltworksFlagstones", n, n, true, false, TextureWrapMode.Repeat), px);
            flags.Normal = NormalMap("SaltworksFlagstonesN", h, n, 2.6f);
            return flags;
        }

        /// <summary>
        /// Steel deck plate, one repeat = 2 m: 2 x 2 welded plates with a raised diagonal tread pattern, worn bright on
        /// the tread tops, darker paint between, rust bloom near the seams. A = smoothness.
        /// </summary>
        public static Pair DeckPlate()
        {
            if (deck.Albedo != null) return deck;
            const int n = 256;
            var px = new Color32[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    int lx = x % 128, ly = y % 128;
                    float edge = Mathf.Min(Mathf.Min(lx, 127 - lx), Mathf.Min(ly, 127 - ly));
                    // Treads: short raised bars, alternating direction on a 16 px lattice.
                    int cx = x / 16, cy = y / 16;
                    float fx = (x % 16) - 7.5f, fy = (y % 16) - 7.5f;
                    bool alt = ((cx + cy) & 1) == 0;
                    float a = alt ? (fx + fy) * 0.7071f : (fx - fy) * 0.7071f;
                    float b = alt ? (fx - fy) * 0.7071f : (fx + fy) * 0.7071f;
                    float tread = Mathf.Clamp01(1f - Mathf.Max(Mathf.Abs(a) / 5.5f, Mathf.Abs(b) / 1.6f));
                    tread = Smooth(0f, 0.35f, tread);
                    float fine = Fbm(u, v, 32, 3, 121);
                    float rust = Fbm(u, v, 8, 4, 122) - Mathf.Clamp01(edge / 30f) * 0.3f;
                    float c = 0.55f + (fine - 0.5f) * 0.08f + tread * 0.22f;
                    float smooth = 0.45f + tread * 0.3f;
                    float height = tread * 1.2f + fine * 0.15f;
                    if (rust > 0.55f)
                    {
                        c *= 0.75f;
                        smooth *= 0.4f;
                    }
                    if (edge < 2f)
                    {
                        c *= 0.55f;
                        height = -1.2f;
                        smooth = 0.2f;
                    }
                    px[y * n + x] = new Color32(B(c), B(c * (rust > 0.55f ? 0.9f : 1f)), B(c * (rust > 0.55f ? 0.82f : 1.02f)), B(smooth));
                    h[y * n + x] = height;
                }
            }
            deck.Albedo = Finish(New("SaltworksDeckPlate", n, n, true, false, TextureWrapMode.Repeat), px);
            deck.Normal = NormalMap("SaltworksDeckPlateN", h, n, 2.2f);
            return deck;
        }

        /// <summary>
        /// Timber decking, one repeat = 2 m: ten boards with staggered butt joints, grain, knots, dark gaps and
        /// slightly varied weathering per board. A = smoothness.
        /// </summary>
        public static Pair Timber()
        {
            if (timber.Albedo != null) return timber;
            const int n = 256;
            const int boards = 10;
            const float bwF = n / (float)boards;
            var px = new Color32[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    int board = Mathf.Min(boards - 1, (int)(x / bwF));
                    float lx = x - board * bwF;
                    float edgeX = Mathf.Min(lx, bwF - 1f - lx);
                    // One butt joint per board per repeat, staggered.
                    int joint = (int)(Hash(board, 0, 131) * n);
                    int dj = Mathf.Abs(y - joint);
                    dj = Mathf.Min(dj, n - dj);
                    float tone = 0.74f + (Hash(board, 1, 132) - 0.5f) * 0.18f;
                    float grain = Mathf.Sin((u * 40f + Fbm(u, v, 4, 3, 133 + board) * 3f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                    float fine = Fbm(u, v, 32, 3, 134);
                    float c = tone * (0.9f + grain * 0.1f) + (fine - 0.5f) * 0.06f;
                    float height = grain * 0.25f + fine * 0.2f;
                    float smooth = 0.2f;
                    // A knot now and then.
                    float kx = (board + 0.5f) * bwF, ky = Hash(board, 2, 135) * n;
                    float kd = new Vector2(x - kx, Mathf.Min(Mathf.Abs(y - ky), n - Mathf.Abs(y - ky))).magnitude;
                    if (Hash(board, 3, 136) < 0.5f && kd < 5f) c *= Mathf.Lerp(0.6f, 1f, kd / 5f);
                    if (edgeX < 1.6f || dj < 1)
                    {
                        c *= 0.35f;
                        height = -1.4f;
                        smooth = 0.05f;
                    }
                    else if (edgeX < 3f) c *= 0.9f;
                    px[y * n + x] = new Color32(B(c), B(c * 0.86f), B(c * 0.68f), B(smooth));
                    h[y * n + x] = height;
                }
            }
            timber.Albedo = Finish(New("SaltworksTimber", n, n, true, false, TextureWrapMode.Repeat), px);
            timber.Normal = NormalMap("SaltworksTimberN", h, n, 2.4f);
            return timber;
        }

        /// <summary>Fine grain for guns and armour (A = smoothness variation).</summary>
        public static Texture2D Grain()
        {
            if (grain != null) return grain;
            const int n = 64;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float f = Fbm(x / (float)n, y / (float)n, 8, 3, 71);
                float c = 0.92f + (f - 0.5f) * 0.12f;
                px[y * n + x] = new Color32(B(c), B(c), B(c), B(0.8f + (f - 0.5f) * 0.3f));
            }
            grain = Finish(New("SaltworksGrain", n, n, true, false, TextureWrapMode.Repeat), px);
            return grain;
        }

        // ---- effects -----------------------------------------------------------------------------
        /// <summary>A bullet hole: dark core, a cracked chipped ring, transparent outside.</summary>
        public static Texture2D BulletHole()
        {
            if (hole != null) return hole;
            const int n = 64;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var d = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f);
                float r = d.magnitude / (n / 2f);
                float ang = Mathf.Atan2(d.y, d.x);
                float cracks = Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * 5f + Mathf.Sin(ang * 3f) * 1.3f)), 18f);
                float a = 0f, c = 0.05f;
                if (r < 0.16f) { a = 1f; c = 0.02f; }
                else if (r < 0.36f) { a = Mathf.Lerp(0.85f, 0.35f, (r - 0.16f) / 0.2f); c = 0.18f; }
                if (r < 0.9f) a = Mathf.Max(a, cracks * (1f - r) * 0.8f);
                if (r >= 0.36f && r < 0.6f) a = Mathf.Max(a, 0.18f * (1f - (r - 0.36f) / 0.24f));
                px[y * n + x] = new Color32(B(c), B(c), B(c), B(a));
            }
            hole = Finish(New("SaltworksBulletHole", n, n, true, false, TextureWrapMode.Clamp), px);
            return hole;
        }

        /// <summary>A soft round dot (white, alpha falloff) for sparks, dust and glows.</summary>
        public static Texture2D SoftDot()
        {
            if (dot != null) return dot;
            const int n = 32;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float r = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).magnitude / (n / 2f);
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - 2f * a);
                px[y * n + x] = new Color32(255, 255, 255, B(a));
            }
            dot = Finish(New("SaltworksSoftDot", n, n, true, false, TextureWrapMode.Clamp), px);
            return dot;
        }

        /// <summary>A muzzle-flash star: a hot core with six tapering petals.</summary>
        public static Texture2D FlashStar()
        {
            if (star != null) return star;
            const int n = 64;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var d = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f) / (n / 2f);
                float r = d.magnitude;
                float ang = Mathf.Atan2(d.y, d.x);
                float petals = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 3f)), 3f);
                float reach = 0.35f + 0.65f * petals;
                float a = Mathf.Clamp01(1f - r / reach);
                a = a * a;
                float core = Mathf.Clamp01(1f - r / 0.3f);
                a = Mathf.Max(a, core);
                byte g = B(0.85f + 0.15f * core);
                byte bl = B(0.55f + 0.45f * core);
                px[y * n + x] = new Color32(255, g, bl, B(a));
            }
            star = Finish(New("SaltworksFlashStar", n, n, true, false, TextureWrapMode.Clamp), px);
            return star;
        }

        /// <summary>A tracer: bright along the centre line (V), soft to the edges.</summary>
        public static Texture2D Tracer()
        {
            if (tracer != null) return tracer;
            const int w = 4, h = 32;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;
                float a = Mathf.Clamp01(1f - v);
                a = a * a;
                for (int x = 0; x < w; x++) px[y * w + x] = new Color32(255, 255, 255, B(a));
            }
            tracer = Finish(New("SaltworksTracer", w, h, false, false, TextureWrapMode.Clamp), px);
            return tracer;
        }

        /// <summary>A soft horizontal light strip: bright core, glow fading to the top and bottom edges.</summary>
        public static Texture2D GlowStrip()
        {
            if (glowStrip != null) return glowStrip;
            const int w = 4, h = 32;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;
                float a = Mathf.Clamp01(1f - v);
                a = Mathf.Max(a * a * 0.5f, v < 0.18f ? 1f : 0f);
                for (int x = 0; x < w; x++) px[y * w + x] = new Color32(255, 255, 255, B(a));
            }
            glowStrip = Finish(New("SaltworksGlowStrip", w, h, false, false, TextureWrapMode.Clamp), px);
            return glowStrip;
        }

        /// <summary>Yellow and charcoal diagonal hazard stripes with worn edges (one repeat = one stripe pair).</summary>
        public static Texture2D Hazard()
        {
            if (hazard != null) return hazard;
            const int n = 64;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                bool yellow = Mathf.Repeat(u + v, 1f) < 0.5f;
                float wear = Fbm(u, v, 8, 3, 81);
                float a = Mathf.Clamp01(0.92f - Mathf.Max(0f, wear - 0.62f) * 2.5f);
                Color c = yellow ? new Color(0.98f, 0.76f, 0.12f) : new Color(0.12f, 0.12f, 0.13f);
                px[y * n + x] = new Color32(B(c.r), B(c.g), B(c.b), B(a));
            }
            hazard = Finish(New("SaltworksHazard", n, n, true, false, TextureWrapMode.Repeat), px);
            return hazard;
        }

        /// <summary>The spike plant zone stencil: a double ring, corner ticks, inner diagonal hatching; white on clear.</summary>
        public static Texture2D PlantZone()
        {
            if (plant != null) return plant;
            const int n = 256;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var d = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f) / (n / 2f);
                float r = d.magnitude;
                float a = 0f;
                a = Mathf.Max(a, Band(r, 0.94f, 0.025f));
                a = Mathf.Max(a, Band(r, 0.86f, 0.012f) * 0.8f);
                // Dashes between the rings.
                float ang = Mathf.Atan2(d.y, d.x);
                if (r > 0.88f && r < 0.92f && Mathf.Repeat(ang / (Mathf.PI * 2f) * 48f, 1f) < 0.5f) a = Mathf.Max(a, 0.7f);
                // Hatching inside, fading to the centre so the site letter stays readable.
                if (r < 0.84f)
                {
                    float hatch = Mathf.Repeat((d.x + d.y) * 9f, 1f) < 0.16f ? 1f : 0f;
                    a = Mathf.Max(a, hatch * 0.22f * Smooth(0.35f, 0.8f, r));
                }
                float wear = Fbm(x / (float)n, y / (float)n, 8, 3, 91);
                a *= Mathf.Lerp(0.55f, 1f, Smooth(0.3f, 0.6f, wear));
                px[y * n + x] = new Color32(255, 255, 255, B(a));
            }
            plant = Finish(New("SaltworksPlantZone", n, n, true, false, TextureWrapMode.Clamp), px);
            return plant;
        }

        static float Band(float r, float centre, float half)
        {
            return Mathf.Clamp01(1f - Mathf.Abs(r - centre) / half);
        }

        /// <summary>The scope: black outside a circle (radius 0.48), clear inside, a soft dark lens edge.</summary>
        public static Texture2D ScopeMask()
        {
            if (scope != null) return scope;
            const int n = 512;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float r = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).magnitude / (n / 2f);
                float a = Smooth(0.84f, 0.96f, r);
                px[y * n + x] = new Color32(0, 0, 0, B(a));
            }
            scope = Finish(New("SaltworksScopeMask", n, n, false, false, TextureWrapMode.Clamp), px);
            return scope;
        }
    }
}
