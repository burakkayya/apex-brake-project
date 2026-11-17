using System.Collections;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ApexBrake.Racing;
using ApexBrake.Simulation;
using ApexBrake.Track;

namespace ApexBrake.Tests.PlayMode
{
    /// <summary>Geliştirme ölçümü (Explicit): botların kanaat çarpanına (Courage) göre yarış pistinde bitirme/çıkma ve süreleri.
    /// Hata/kusursuz geçiş kapalı, beceri tam: yalnızca aracın gerçek sınırını bulmak için.</summary>
    [Explicit]
    public sealed class BotTuningSweep
    {
        [UnityTest]
        public IEnumerator CourageSweep()
        {
            var inv = CultureInfo.InvariantCulture;
            var lines = new System.Text.StringBuilder();
            foreach (var batch in new[] { new[] { 1.0f, 1.2f, 1.4f, 1.6f, 1.8f }, new[] { 2.0f, 2.3f, 2.6f, 3.0f, 4.0f } })
            {
                using (var rig = new BotDriverTests.SoloRig(GrayboxLayouts.Race))
                {
                    for (int i = 0; i < batch.Length; i++)
                    {
                        var p = new BotProfile { Name = "C" + batch[i].ToString("0.0", inv), Skill = 1f, Courage = batch[i], MistakeChance = 0f, HeroChance = 0f, ReactionTime = 0.06f, Wander = 0f };
                        rig.Add(p, 1, 2, 10f + i * 14f);    // aynı şerit, arka arkaya (çarpışmalar kapalı)
                    }
                    yield return rig.Race(90f);
                    foreach (var r in rig.Runners)
                        lines.AppendLine($"{r.Profile.Name}: " + (r.Finished ? "finished " + r.FinishTime.ToString("0.00", inv) + " from100m " + (r.FinishTime - r.PassTime).ToString("0.00", inv) + " maxSlip " + r.MaxSlip.ToString("0", inv) : r.Out ? $"OUT {r.Reason} at {r.OutDistance:0}" : "running"));
                }
            }
            Debug.Log("[BotTuningSweep]\n" + lines);
        }
    }
}
