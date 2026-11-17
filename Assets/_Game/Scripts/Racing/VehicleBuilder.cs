using UnityEngine;
using ApexBrake.Track;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Fizik aracı kodla kurar: kök (Rigidbody, VehicleMotor, LineDriver, VehicleTelemetry), gövde ve kabin kutu
    /// çarpışmaları, 4 WheelCollider ve görsel tekerlekler. Kök orijini teker temas düzlemindedir.
    /// CarPrefabFactory (Editor) ve testler aynı kodu kullanır.
    /// </summary>
    public static class VehicleBuilder
    {
        static Material _wheelMat, _cabinMat, _lightMat, _tailMat, _smokeMat;

        public static VehicleMotor Create(VehicleConfig config, Color color, string name = "Car")
        {
            var root = new GameObject(name);
            root.SetActive(false);
            root.layer = TrackLayers.Car;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = config.mass;

            var bodyMat = MakeMat(color);
            if (_cabinMat == null) _cabinMat = MakeMat(new Color(0.12f, 0.13f, 0.16f));
            if (_wheelMat == null) _wheelMat = MakeMat(new Color(0.08f, 0.08f, 0.08f));
            if (_lightMat == null) _lightMat = MakeMat(new Color(1f, 0.95f, 0.7f));

            // Gövde
            float bodyY = config.bodyGroundClearance + config.bodySize.y * 0.5f;
            var body = Box("Body", root.transform, new Vector3(0f, bodyY, 0f), config.bodySize, bodyMat, true);
            float cabinY = config.bodyGroundClearance + config.bodySize.y + config.cabinSize.y * 0.5f;
            Box("Cabin", root.transform, new Vector3(0f, cabinY, -0.25f), config.cabinSize, _cabinMat, true);
            // Burun işaretleri (yön görünsün): farlar
            float front = config.bodySize.z * 0.5f;
            Box("LightL", body.transform.parent, new Vector3(-0.55f, bodyY + 0.05f, front + 0.01f), new Vector3(0.35f, 0.12f, 0.04f), _lightMat, false);
            Box("LightR", body.transform.parent, new Vector3(0.55f, bodyY + 0.05f, front + 0.01f), new Vector3(0.35f, 0.12f, 0.04f), _lightMat, false);
            // Stop lambaları (VehicleVisual frende yakar)
            if (_tailMat == null) _tailMat = MakeTailMat();
            var tails = new Renderer[]
            {
                Box("BrakeLightL", body.transform.parent, new Vector3(-0.6f, bodyY + 0.08f, -front - 0.01f), new Vector3(0.45f, 0.14f, 0.04f), _tailMat, false).GetComponent<Renderer>(),
                Box("BrakeLightR", body.transform.parent, new Vector3(0.6f, bodyY + 0.08f, -front - 0.01f), new Vector3(0.45f, 0.14f, 0.04f), _tailMat, false).GetComponent<Renderer>(),
            };

            // Tekerlekler: dinlenmede teker merkezi ≈ yarıçap yüksekliğinde.
            var wheelRoot = new GameObject("Wheels").transform;
            wheelRoot.SetParent(root.transform, false);
            var colliders = new WheelCollider[4];
            var visuals = new Transform[4];
            string[] names = { "FL", "FR", "RL", "RR" };
            float hx = config.trackWidth * 0.5f, hz = config.wheelBase * 0.5f;
            // WheelCollider dinlenme konumu (targetPosition) normal yerçekimine göredir; ek yerçekimi kuvvetinin
            // yaydaki ek çökmesi (statik sarkma) bağlantı yüksekliğine eklenir ki gövde tasarlanan yükseklikte dursun.
            float extraSag = (config.gravityMultiplier - 1f) * Physics.gravity.magnitude * config.mass * 0.25f / Mathf.Max(config.spring, 1f);
            float restDrop = config.suspensionDistance * (1f - config.targetPosition) - extraSag;
            float mountY = config.wheelRadius + restDrop;
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0) ? -hx : hx;
                float z = i < 2 ? hz : -hz;
                var wgo = new GameObject("Wheel_" + names[i]);
                wgo.layer = TrackLayers.Car;
                wgo.transform.SetParent(wheelRoot, false);
                wgo.transform.localPosition = new Vector3(x, mountY, z);
                colliders[i] = wgo.AddComponent<WheelCollider>();
                colliders[i].radius = config.wheelRadius;
                colliders[i].suspensionDistance = config.suspensionDistance;

                var vis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(vis.GetComponent<Collider>());
                vis.name = "Visual_" + names[i];
                vis.transform.SetParent(wheelRoot, false);
                vis.transform.localPosition = new Vector3(x, config.wheelRadius, z);
                var mesh = vis.transform;
                // Silindir Y ekseninde; tekerlek X ekseninde döner → çocuk olarak 90° çevrilmiş mesh.
                var holder = new GameObject("Wheel_" + names[i] + "_Visual").transform;
                holder.SetParent(wheelRoot, false);
                holder.localPosition = mesh.localPosition;
                mesh.SetParent(holder, false);
                mesh.localPosition = Vector3.zero;
                mesh.localRotation = Quaternion.Euler(0f, 0f, 90f);
                mesh.localScale = new Vector3(config.wheelRadius * 2f, 0.12f, config.wheelRadius * 2f); // silindir yüksekliği 2 birim → 0,24 m lastik
                mesh.GetComponent<MeshRenderer>().sharedMaterial = _wheelMat;
                // Jant işareti (dönüş görünsün)
                var spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(spoke.GetComponent<Collider>());
                spoke.name = "Spoke";
                spoke.transform.SetParent(holder, false);
                spoke.transform.localScale = new Vector3(0.3f, config.wheelRadius * 1.6f, 0.08f);
                spoke.GetComponent<MeshRenderer>().sharedMaterial = _lightMat;
                visuals[i] = holder;
            }

            var motor = root.AddComponent<VehicleMotor>();
            motor.config = config;
            motor.Bind(colliders, visuals);
            var bodyPm = new PhysicsMaterial("CarBody")
            {
                dynamicFriction = config.bodyFriction, staticFriction = config.bodyFriction, bounciness = 0.05f,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            foreach (var bc in root.GetComponentsInChildren<BoxCollider>(true)) bc.sharedMaterial = bodyPm;
            root.AddComponent<LineDriver>();
            root.AddComponent<VehicleTelemetry>();
            var smoke = new ParticleSystem[4];
            for (int i = 0; i < 4; i++) smoke[i] = MakeSmoke(wheelRoot, colliders[i].transform.localPosition, names[i]);
            root.AddComponent<VehicleVisual>().Bind(tails, smoke);
            SetLayerRecursive(root, TrackLayers.Car);
            root.SetActive(true);
            return motor;
        }

        static Material MakeTailMat()
        {
            var m = MakeMat(new Color(0.35f, 0.04f, 0.04f));
            m.name = "BrakeLight";
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            return m;
        }

        /// <summary>Teker altında duman: dünya uzayında, yavaş yükselip büyüyen gri yumaklar (gri kutu düzeyinde).</summary>
        static ParticleSystem MakeSmoke(Transform parent, Vector3 localPos, string name)
        {
            var go = new GameObject("Smoke_" + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(localPos.x, 0.1f, localPos.z);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f; main.loop = true; main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startColor = new Color(0.82f, 0.82f, 0.82f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f;
            main.maxParticles = 200;
            var em = ps.emission; em.rateOverTime = 0f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.2f;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (_smokeMat == null) _smokeMat = MakeSmokeMat();
            r.sharedMaterial = _smokeMat;
            ps.Play();
            return ps;
        }

        static Material MakeSmokeMat()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var m = new Material(shader) { name = "TireSmoke" };
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "TireSmokeTex", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float d = new Vector2(x - 15.5f, y - 15.5f).magnitude / 15.5f;
                    float a = Mathf.Clamp01(1f - d); a *= a;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            return m;
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }

        static Material MakeMat(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
