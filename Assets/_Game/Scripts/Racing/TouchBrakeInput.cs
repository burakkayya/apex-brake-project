using UnityEngine;
using UnityEngine.InputSystem;

namespace ApexBrake.Racing
{
    /// <summary>Herhangi bir parmak ekrandaysa (ya da fare/Space basılıysa) fren. Her karede yoklanır.</summary>
    public sealed class TouchBrakeInput : MonoBehaviour
    {
        public bool IsBraking { get; private set; }

        void Update() => IsBraking = Poll();

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
