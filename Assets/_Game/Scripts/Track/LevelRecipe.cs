using System.Collections.Generic;
using UnityEngine;

namespace ApexBrake.Track
{
    [CreateAssetMenu(menuName = "Apex Brake/Level Recipe")]
    public sealed class LevelRecipe : ScriptableObject
    {
        public List<TrackPieceAsset> pieces = new List<TrackPieceAsset>();
    }
}
