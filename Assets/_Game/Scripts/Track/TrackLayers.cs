using UnityEngine;

namespace ApexBrake.Track
{
    /// <summary>
    /// Fizik katmanları (sabit indeksler; adları Editor'de ProjectPhysicsSetup TagManager'a yazar, derlemeye girer)
    /// ve yüzey fizik malzemeleri. Katman çarpışma matrisi oyun açılışında koddan kurulur.
    /// </summary>
    public static class TrackLayers
    {
        public const int Road = 8;     // yol + banket
        public const int Ground = 9;   // yol dışı zemin
        public const int Car = 10;

        public const string RoadName = "Road", GroundName = "Ground", CarName = "Car";

        public const int RoadMask = 1 << Road;
        public const int GroundMask = 1 << Ground;
        public const int SurfaceMask = RoadMask | GroundMask;

        /// <summary>Yol sürtünmesi (asfalt). Tekerlek sürtünmesi bu değere göre ölçeklenir.</summary>
        public const float RoadFriction = 1.0f;
        /// <summary>Yol dışı zemin (çakıl/çim): araç burada tutunamaz.</summary>
        public const float GroundFriction = 0.4f;

        static PhysicsMaterial _road, _ground;

        public static PhysicsMaterial RoadMaterial => _road != null ? _road : (_road = Make("Road", RoadFriction));
        public static PhysicsMaterial GroundMaterial => _ground != null ? _ground : (_ground = Make("Ground", GroundFriction));

        static PhysicsMaterial Make(string name, float mu) => new PhysicsMaterial(name)
        {
            dynamicFriction = mu,
            staticFriction = mu,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Minimum,
        };

        /// <summary>Malzemenin sürtünmesinin yol sürtünmesine oranı (malzeme yoksa 1).</summary>
        public static float FrictionRatio(Collider c)
        {
            if (c == null) return 1f;
            var m = c.sharedMaterial;
            if (m == null) return c.gameObject.layer == Ground ? GroundFriction / RoadFriction : 1f;
            return m.dynamicFriction / RoadFriction;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void ApplyCollisionMatrix()
        {
            // Araçlar yol, zemin ve birbirleriyle çarpışır; yol ile zemin statiktir (aralarında temas gerekmez).
            Physics.IgnoreLayerCollision(Car, Road, false);
            Physics.IgnoreLayerCollision(Car, Ground, false);
            Physics.IgnoreLayerCollision(Car, Car, false);
            Physics.IgnoreLayerCollision(Car, 0, false);
            Physics.IgnoreLayerCollision(Road, Ground, true);
            Physics.IgnoreLayerCollision(Road, Road, true);
            Physics.IgnoreLayerCollision(Ground, Ground, true);
        }
    }
}
