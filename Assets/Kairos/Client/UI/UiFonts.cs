using UnityEngine;

namespace Kairos.Client
{
    // Bundled fonts, independent of fonts installed on the player's operating system.
    public static class UiFonts
    {
        static Font regular, bold;
        public static Font Regular => regular != null ? regular : regular = Load("MiSans-Regular");
        public static Font Bold => bold != null ? bold : bold = Load("MiSans-Bold");
        static Font Load(string name)
        {
            var font = Resources.Load<Font>("Fonts/" + name);
            if (font == null) throw new System.InvalidOperationException("Missing bundled UI font: Fonts/" + name);
            return font;
        }
        public static bool IsFallback(Font font) => font == null || font.name == "Arial" || font.name == "LegacyRuntime";
    }
}
