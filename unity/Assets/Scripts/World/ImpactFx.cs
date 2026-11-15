// Bullet impacts and brass, all pooled: a ring buffer of bullet-hole decals
// (one shared quad mesh and material, so they batch), and three particle
// systems emitted by hand (sparks, dust puffs, mesh shells that bounce on the
// map's colliders). Nothing here allocates per shot.
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    public class ImpactFx : MonoBehaviour
    {
        const int DecalCount = 96;

        Transform root;
        readonly Transform[] decals = new Transform[DecalCount];
        int nextDecal;
        Mesh quad;
        Material decalMat;
        ParticleSystem sparks, dust, shells;
        AudioSource tinkSource;
        AudioClip tink;
        float nextTink;
        int lastFlashFrame = -1;

        void Awake()
        {
            root = new GameObject("Impacts").transform;
            quad = Quad();
            decalMat = Mats.Decal(Textures.BulletHole(), Color.white);
            for (int i = 0; i < DecalCount; i++)
            {
                var go = new GameObject("Decal");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = decalMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                go.SetActive(false);
                decals[i] = go.transform;
            }
            sparks = MakeSystem("Sparks", Mats.Additive(Textures.SoftDot(), Color.white, 1.6f, 3150), 400, 1.6f);
            var sr = sparks.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch;
            sr.velocityScale = 0.035f;
            sr.lengthScale = 1.2f;

            dust = MakeSystem("Dust", Mats.Decal(Textures.SoftDot(), Color.white, 3140), 200, -0.05f);
            var sol = dust.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.8f));

            Material brass = Mats.World(new Color(0.85f, 0.64f, 0.3f), null, null, 0f, 0.5f, 0.9f, 0.75f, 0f);
            shells = MakeSystem("Shells", brass, 120, 1.4f);
            var shr = shells.GetComponent<ParticleSystemRenderer>();
            shr.renderMode = ParticleSystemRenderMode.Mesh;
            shr.mesh = ShellMesh();
            shr.alignment = ParticleSystemRenderSpace.World;
            shr.shadowCastingMode = ShadowCastingMode.Off;
            var main = shells.main;
            main.startRotation3D = true;
            var col = shells.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.bounce = 0.35f;
            col.dampen = 0.45f;
            col.quality = ParticleSystemCollisionQuality.Low;
            col.radiusScale = 0.5f;
            var shellFade = shells.colorOverLifetime;
            shellFade.enabled = false;

            tinkSource = gameObject.AddComponent<AudioSource>();
            tinkSource.playOnAwake = false;
            tinkSource.spatialBlend = 0f;
            tink = Synth.Tink();
        }

        ParticleSystem MakeSystem(string name, Material mat, int max, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.startLifetime = 0.4f;
            main.startSpeed = 0f;
            main.startSize = 0.05f;
            var em = ps.emission;
            em.enabled = false;
            var sh = ps.shape;
            sh.enabled = false;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(g);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static Mesh Quad()
        {
            var m = new Mesh { name = "Decal quad" };
            m.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            m.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            m.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh ShellMesh()
        {
            var k = new MeshKit(1);
            k.Cylinder(0, Matrix4x4.identity, 0.0055f, 0.022f, 8, Color.white, true);
            return k.ToMesh("Shell");
        }

        /// <summary>A bullet hitting the map: a hole decal, sparks off the surface and a little dust.</summary>
        public void Impact(Vector3 p, Vector3 n, Vector3 dir)
        {
            if (n.sqrMagnitude < 1e-6f) n = -dir;
            n.Normalize();
            Transform d = decals[nextDecal];
            nextDecal = (nextDecal + 1) % DecalCount;
            d.gameObject.SetActive(true);
            d.position = p + n * 0.004f;
            d.rotation = Quaternion.LookRotation(-n) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float s = Random.Range(0.07f, 0.11f);
            d.localScale = new Vector3(s, s, s);
            // One spark light per frame at most (pellets hit together).
            if (lastFlashFrame != Time.frameCount)
            {
                lastFlashFrame = Time.frameCount;
                FxLights.Flash(p + n * 0.12f, new Color(1f, 0.72f, 0.38f), 1.1f, 2.6f, 0.06f);
            }

            Vector3 refl = Vector3.Reflect(dir.normalized, n);
            int count = Random.Range(5, 10);
            for (int i = 0; i < count; i++)
            {
                Vector3 v = (refl * 0.6f + n * 0.6f + Random.insideUnitSphere * 0.7f).normalized * Random.Range(2.5f, 7f);
                var ep = new ParticleSystem.EmitParams
                {
                    position = p + n * 0.01f,
                    velocity = v,
                    startSize = Random.Range(0.012f, 0.025f),
                    startLifetime = Random.Range(0.12f, 0.3f),
                    startColor = new Color(1f, Random.Range(0.6f, 0.85f), 0.35f, 1f),
                    applyShapeToPosition = false,
                };
                sparks.Emit(ep, 1);
            }
            for (int i = 0; i < 2; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = p + n * 0.03f,
                    velocity = (n * 0.6f + Random.insideUnitSphere * 0.25f),
                    startSize = Random.Range(0.12f, 0.2f),
                    startLifetime = Random.Range(0.45f, 0.7f),
                    startColor = new Color(0.72f, 0.7f, 0.66f, 0.45f),
                    applyShapeToPosition = false,
                };
                dust.Emit(ep, 1);
            }
        }

        /// <summary>A hit on an agent: a short burst of sparks in their colour (no blood, stylised armour).</summary>
        public void BodyHit(Vector3 p, Vector3 dir, Color c)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 v = (-dir.normalized * 0.4f + Random.insideUnitSphere).normalized * Random.Range(1.5f, 4.5f);
                var ep = new ParticleSystem.EmitParams
                {
                    position = p,
                    velocity = v,
                    startSize = Random.Range(0.02f, 0.04f),
                    startLifetime = Random.Range(0.12f, 0.25f),
                    startColor = Color.Lerp(c, Color.white, 0.4f),
                    applyShapeToPosition = false,
                };
                sparks.Emit(ep, 1);
            }
        }

        /// <summary>An ejected shell (world space), tumbling, bouncing on the floor.</summary>
        public void Shell(Vector3 p, Vector3 v, float scale = 1f)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = p,
                velocity = v,
                startSize = scale,
                startLifetime = 1.4f,
                startColor = Color.white,
                rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                angularVelocity3D = new Vector3(Random.Range(-900f, 900f), Random.Range(-900f, 900f), Random.Range(-900f, 900f)),
                applyShapeToPosition = false,
            };
            shells.Emit(ep, 1);
            if (Time.time >= nextTink)
            {
                nextTink = Time.time + 0.08f;
                tinkSource.pitch = Random.Range(0.9f, 1.15f);
                tinkSource.clip = tink;
                tinkSource.PlayDelayed(Random.Range(0.35f, 0.55f));
            }
        }

        /// <summary>Removes every decal and particle (a new map or round).</summary>
        public void Clear()
        {
            for (int i = 0; i < DecalCount; i++) if (decals[i] != null) decals[i].gameObject.SetActive(false);
            if (sparks != null) sparks.Clear();
            if (dust != null) dust.Clear();
            if (shells != null) shells.Clear();
        }
    }
}
