using UnityEngine;
using UnityEngine.InputSystem;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Herhangi bir parmak ekrandaysa (Editor/masaüstünde sol fare tuşu ya da Space) fren.
    /// Olay yerine her okumada yoklanır: yarış başlamadan basılı tutulan parmak doğru okunur,
    /// bırakma olayı kaçsa bile takılı kalmaz. Arayüz üstündeki dokunma da frendir.
    /// </summary>
    public sealed class TouchBrakeInput : MonoBehaviour
    {
        /// <summary>Test kancası: true iken fren basılı sayılır (Editor'den betikle doğrulama için).</summary>
        public bool ForceBrake;

        public bool IsBraking => ForceBrake || Poll();

        static bool Poll()
        {
            var ts = Touchscreen.current;
            if (ts != null)
            {
                var touches = ts.touches;
                for (int i = 0; i < touches.Count; i++)
                    if (touches[i].press.isPressed) return true;
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed) return true;
            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.isPressed) return true;
            return false;
        }
    }
}
