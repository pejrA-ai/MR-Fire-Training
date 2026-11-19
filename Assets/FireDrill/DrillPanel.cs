using System;
using UnityEngine;
using UnityEngine.UI;

namespace FireDrillMR
{
    public sealed class DrillPanel : MonoBehaviour
    {
        [SerializeField] Text title, detail, controls, source, pressureValue, fireValue, state;
        [SerializeField] Image pressureBar, fireBar, statusStripe;
        string currentBindings;
        static readonly Color Amber = new Color32(255, 164, 90, 255);
        static readonly Color Mint = new Color32(117, 236, 198, 255);

        public void SetCamera(Camera camera) => GetComponent<Canvas>().worldCamera = camera;

        public void Refresh(string heading, string instruction, string bindings, string input, float pressure,
            float fire, bool aimed, bool complete)
        {
            Set(title, heading);
            Set(detail, instruction);
            if (currentBindings != bindings)
            {
                Set(controls, FormatBindings(bindings));
                currentBindings = bindings;
            }
            Set(source, input);
            Set(pressureValue, $"{Mathf.RoundToInt(100 * pressure):00}<size=28>%</size>");
            Set(fireValue, $"{(fire > 0 ? Mathf.Max(1, Mathf.RoundToInt(100 * fire)) : 0):00}<size=28>%</size>");
            Set(state, complete ? "COMPLETE" : aimed ? "ON TARGET" : "ACQUIRE TARGET");
            pressureBar.rectTransform.localScale = new Vector3(pressure, 1, 1);
            fireBar.rectTransform.localScale = new Vector3(fire, 1, 1);
            state.color = statusStripe.color = complete || aimed ? Mint : Amber;
        }

        static string FormatBindings(string bindings)
        {
            var shortcuts = bindings.Split(new[] { "    " }, StringSplitOptions.None);
            for (int i = 0; i < shortcuts.Length; i++)
            {
                string shortcut = shortcuts[i];
                int separator = shortcut.IndexOf("  ", StringComparison.Ordinal);
                if (separator <= 0 || shortcut.Length <= separator + 2) continue;
                string action = shortcut.Substring(separator + 2);
                shortcuts[i] = $"<b>{shortcut.Substring(0, separator)}</b>  {action[0]}{action.Substring(1).ToLowerInvariant()}";
            }

            return string.Join("     ", shortcuts);
        }

        static void Set(Text label, string value)
        {
            if (label.text != value) label.text = value;
        }
    }
}
