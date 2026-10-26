using System.Collections.Generic;
using Verse;
using Texture2D = UnityEngine.Texture2D;

namespace JusticiarValor
{
    // The icon of a command button the mod makes in code (Defs/CommandIconDefs): iconPath is a texture path under
    // Textures/, as an ability's iconPath. The texture is loaded once the game has finished loading, as AbilityDef does.
    public class CommandIconDef : Def
    {
        public string iconPath;

        [Unsaved(false)]
        private Texture2D icon;

        public Texture2D Icon => icon ?? BaseContent.BadTex;

        public override void PostLoad()
        {
            base.PostLoad();
            if (!string.IsNullOrEmpty(iconPath))
            {
                LongEventHandler.ExecuteWhenFinished(() => icon = ContentFinder<Texture2D>.Get(iconPath));
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (string.IsNullOrEmpty(iconPath))
            {
                yield return "no iconPath";
            }
        }
    }
}
