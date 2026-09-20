using UnityEditor;
using UnityEngine;

namespace SuperMario.EditorTools
{
    /// <summary>
    /// 精灵导入配置。放在 AssetPostprocessor 里，Art 只要把 PNG 丢进 Resources/Sprites 就会自动配好。
    /// <para>
    /// <b>为什么不用手动在 Inspector 里设</b>：这批素材有 900 多张，手设必漏；
    /// 而漏设的症状是"某几张图糊了"或"人物浮在半空"，非常难查。用后处理器则天然一致。
    /// </para>
    /// <para>
    /// <b>三条关键设置及其后果</b>：
    /// ① <c>spritePixelsPerUnit = 16</c>：素材是 16x16 一格，只有这个值能让"1 格 = 1 世界单位"
    ///    成立，否则马里奥会比瓦片大 100 倍或小 100 倍；
    /// ② <c>FilterMode.Point</c> + 不压缩 + 无 mipmap：像素画用双线性过滤会糊成一团；</para>
    /// ③ 轴心按目录分两套：站地上的（马里奥 / 敌人 / 道具）用<b>底部居中</b>，
    ///    平铺的（瓦片 / 背景 / 特效）用<b>居中</b>。混用会让"脚"和"格子中心"对不上。
    /// </summary>
    public sealed class SpriteImportPostprocessor : AssetPostprocessor
    {
        /// <summary>素材基准：16 像素 = 1 世界单位（1 个瓦片）。</summary>
        private const int PixelsPerUnit = 16;

        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Sprites/")) return;

            var importer = (TextureImporter)assetImporter;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.sRGBTexture = true;

            // 轴心规则。
            // 注意：`spriteAlignment` / `spritePivot` 不在 TextureImporter 上，而在
            // TextureImporterSettings 里 —— 直接写 importer.spriteAlignment 编译不过。
            // 必须 ReadTextureSettings → 改 → SetTextureSettings 这套往返。
            var path = assetPath.Replace('\\', '/');
            var bottomPivot = path.Contains("/Sprites/Mario/")
                              || path.Contains("/Sprites/Enemies/")
                              || path.Contains("/Sprites/Items/");

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (bottomPivot)
            {
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = new Vector2(0.5f, 0f);   // 底部居中 = 脚底
            }
            else
            {
                settings.spriteAlignment = (int)SpriteAlignment.Center;
            }
            importer.SetTextureSettings(settings);
        }
    }
}
