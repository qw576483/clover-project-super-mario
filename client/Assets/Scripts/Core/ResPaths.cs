namespace SuperMario.Core
{
    /// <summary>
    /// 资源路径常量（相对 <c>Resources/</c>，因为 <c>CloverRes.Init("")</c> 以 Resources 根为根）。
    /// <para>
    /// 路径字面量只允许出现在这里。散落的路径字符串是「换素材要全局搜」的根源，
    /// 而换素材恰恰是这个项目最可能发生的事（占位 → 真素材）。
    /// </para>
    /// </summary>
    public static class ResPaths
    {
        /// <summary>关卡数据文件（纯文本，见 <c>Resources/Levels/World1-1.txt</c>）。</summary>
        public const string Level11 = "Levels/World1-1";

        /// <summary>World 1-2（地下关）。数据从 SMB-clone 工程的 World 1-2.unity 解出。</summary>
        public const string Level12 = "Levels/World1-2";

        /// <summary>
        /// 1-1 的金币房（管中密室）。数据从 SMB-clone 的 `World 1-1 - Underground.unity` 解出
        /// （元素表 §3.2），入口 = 主关卡第 4 根水管（T x=43..44,y=-3..0）。
        /// </summary>
        public const string Level11Underground = "Levels/World1-1-Underground";

        /// <summary>
        /// 1-2 的秘密金币房（管中密室）。数据从 SMB-clone 的 `World 1-2 - Underground.unity` 解出
        /// （元素表 §4.2），入口 = 主关卡那根 `Warp Green Pipe 2x3 Down`
        /// （clone (100.5,0)，`World 1-2.unity:11125` ⇒ 本工程格 x=100..101, y=0..2）。
        /// </summary>
        public const string Level12Underground = "Levels/World1-2-Underground";

        /// <summary>
        /// World 1-2 的**地表段**（原版 clone 的 `World 1-2 - Castle Cut.unity`）。
        /// <para>原版 1-2 是两段：地下段走到底 → 进右侧墙上的侧向管 → 地表段（出管 → 旗杆 → 城堡）。
        /// 旗杆与城堡在**这一段**里（地下段没有），坐标由关卡文件的 `# flagpole` / `# castle` 声明，
        /// 出处见文件头。</para>
        /// <para>⚠️ 这一段在地表，瓦片用 `WorldTileSprites_52`/`_65`（棕砖/大理石）——
        /// 与 1-1 同一套；写成地下段的 `_1`（青砖）会导致"地表段地面是青色的"。</para>
        /// </summary>
        public const string Level12Surface = "Levels/World1-2-Surface";

        /// <summary>
        /// NES 像素字体。**必须由启动期预热**（<c>AppFlow.EnterBoot</c> 的 <c>Preload</c>）——
        /// 字体要在第一个面板出现前就可用，而 <c>Game.Res</c> 只有异步加载，
        /// 所以这里走"先预热、后同步取"（<c>Game.Res.TryGet</c>）的路线。
        /// </summary>
        public const string PixelFont = "Fonts/SuperMarioNES";

        /// <summary>
        /// 引擎署名那行（`by clover-engine`）专用的字体。
        /// <para>
        /// ⚠️ <b>不能用 <see cref="PixelFont"/></b>：那份 NES 像素字体里 a-z 的**字形与 A-Z 完全相同**
        /// （`fontTools` 量得 `b`/`B`、`y`/`Y` … 的轮廓盒逐个相等）⇒ 它把 `by clover-engine`
        /// 渲染成 `BY CLOVER-ENGINE`。全局 skill §1.6 要求署名**逐字**（含大小写），所以这一行
        /// 必须换成一份**真有小写字形**的像素字体。
        /// </para>
        /// <para>
        /// 出处（§0.5 降级链：从原版载体里解析 / 搬运，不是自己造）：
        /// `原版资源/参考工程/SMB-clone/Assets/Fonts/prstart.ttf`（"Press Start"，8×8 像素字体，
        /// 2048 upem；`y`/`g` 的轮廓盒下探 -256 units ⇒ 有真下伸部）→ 按 §1.9 复制成
        /// <c>Resources/Fonts/PressStart2P.ttf</c>（逐字节相同，21320 字节，SHA256 见
        /// `原版资源/清单.md`）。复核命令：`.ai-tmp/test/font_predict.py`（量字形盒）。
        /// </para>
        /// </summary>
        public const string CreditFont = "Fonts/PressStart2P";

        /// <summary>马里奥精灵目录（小 / 大 / 火三套形态，按动作命名）。</summary>
        public const string MarioDir = "Sprites/Mario";

        /// <summary>关卡瓦片精灵目录（地面 / 砖 / 水管）。</summary>
        public const string TileDir = "Sprites/Tiles";

        /// <summary>背景装饰精灵目录（云 / 山丘 / 灌木）。</summary>
        public const string SceneryDir = "Sprites/Scenery";

        /// <summary>敌人精灵目录。</summary>
        public const string EnemyDir = "Sprites/Enemies";

        /// <summary>道具精灵目录（蘑菇 / 花 / 金币 / 星星）。</summary>
        public const string ItemDir = "Sprites/Items";

        /// <summary>城堡与旗杆。</summary>
        public const string CastleDir = "Sprites/Castle";
        public const string FlagDir = "Sprites/Flagpole";

        /// <summary>砖块碎裂粒子。</summary>
        public const string ParticleDir = "Sprites/Particles";

        /// <summary>
        /// 升降台台面（原版 `Moving Platform Vertical`）。
        /// <para>这一件在瓦片图集里没有，是 `misc-3.gif` 的独立切片（`moving_platform_6`，48×8 px），
        /// 由 `原版资源/解析/脚本/gen_platform_sprite.py` 从这个权威载体裁进工程。</para>
        /// </summary>
        public const string PlatformDir = "Sprites/Platform";

        /// <summary>标题画面用的图形（原版 logo）。</summary>
        public const string TitleDir = "Sprites/Title";

        /// <summary>马里奥精灵的完整路径（<paramref name="action"/> 见 <see cref="MarioAction"/>）。</summary>
        public static string Mario(string action) => MarioDir + "/" + action;

        /// <summary>关卡瓦片精灵路径。</summary>
        public static string Tile(string sprite) => TileDir + "/" + sprite;

        /// <summary>背景装饰精灵路径。</summary>
        public static string Scenery(string sprite) => SceneryDir + "/" + sprite;

        /// <summary>敌人精灵路径。</summary>
        public static string Enemy(string sprite) => EnemyDir + "/" + sprite;

        /// <summary>道具精灵路径。</summary>
        public static string Item(string sprite) => ItemDir + "/" + sprite;

        /// <summary>粒子精灵路径。</summary>
        public static string Particle(string sprite) => ParticleDir + "/" + sprite;

        /// <summary>旗杆与旗子精灵路径。</summary>
        public static string Flagpole(string sprite) => FlagDir + "/" + sprite;

        /// <summary>升降台台面精灵路径。</summary>
        public static string Platform(string sprite) => PlatformDir + "/" + sprite;

        /// <summary>城堡精灵路径。</summary>
        public static string Castle(string sprite) => CastleDir + "/" + sprite;

        /// <summary>标题画面图形路径。</summary>
        public static string Title(string sprite) => TitleDir + "/" + sprite;
    }

    /// <summary>
    /// 实体精灵的原始切片名。
    /// <para>
    /// 名字形如 <c>&lt;表名&gt;_&lt;序号&gt;</c>，序号来自原版素材包的切片定义 ——
    /// 每一项都用「预制体引用了哪张图」核对过（不是靠肉眼猜序号），
    /// 所以下面这些常量的出处是可复查的：改素材时对着原素材包重新核对即可。
    /// </para>
    /// </summary>
    public static class SpriteNames
    {
        /// <summary>问号块（未顶）。原版 MysteryBox.prefab 引用。</summary>
        public const string QuestionBlock = "smb1_misc_sprites_76";

        /// <summary>问号块（已顶过）。原版里紧邻上一张的"亮块变暗块"。</summary>
        public const string QuestionBlockUsed = "smb1_misc_sprites_77";

        /// <summary>砖块。原版 Brick.prefab 引用。</summary>
        public const string Brick = "smb1_misc_sprites_13";

        /// <summary>砖块碎裂的三块碎片（原版 BrickParticles.prefab）。</summary>
        public static readonly string[] BrickParticles =
        {
            "BrickParticleSprites_0", "BrickParticleSprites_1", "BrickParticleSprites_2",
        };

        /// <summary>超级蘑菇（原版 Mushroom.prefab 引用）。</summary>
        public const string Mushroom = "smb_items_sheet_13";

        /// <summary>
        /// 1-UP 蘑菇（原版道具表的第 14 号：绿帽白点）。
        /// <para>
        /// 原版本来就有独立贴图。**不要**像这里最早那样"拿超级蘑菇 + 绿色染色"顶替 ——
        /// 那属于用近似物替换真素材，颜色也对不上（原版的绿帽是实色像素，不是乘个绿）。
        /// </para>
        /// </summary>
        public const string OneUp = "smb_items_sheet_14";

        /// <summary>
        /// 无敌星（原版道具表的 23~26 号：同一颗星的四帧颜色循环）。
        /// </summary>
        public static readonly string[] Star =
        {
            "smb_items_sheet_23", "smb_items_sheet_24",
            "smb_items_sheet_25", "smb_items_sheet_26",
        };

        /// <summary>火焰花的循环帧（原版 FireFlower.anim）。</summary>
        public static readonly string[] FireFlower =
        {
            "smb_items_sheet_15", "smb_items_sheet_16", "smb_items_sheet_17",
            "smb_items_sheet_18", "smb_items_sheet_19", "smb_items_sheet_20",
        };

        /// <summary>金币的旋转帧（原版 CoinEffect.anim）。</summary>
        public static readonly string[] Coin =
        {
            "smb1_misc_sprites_82", "smb1_misc_sprites_79", "smb1_misc_sprites_80", "smb1_misc_sprites_81",
        };

        /// <summary>栗宝宝（行走）。</summary>
        public const string GoombaWalk = "smb1_misc_sprites_385";

        /// <summary>栗宝宝（被踩扁）。</summary>
        public const string GoombaFlat = "smb1_misc_sprites_399";

        // ── 1-2 的敌人（编号同样是【看着切片图挑的】，不是按网格推的）──
        // 地下关的乌龟是青色（Teal Koopa），参考画面 Screenshots/world1-2.jpg 里就是它。
        /// <summary>
        /// 绿龟行走的两帧（38 = 一帧、40 = 另一帧）。
        /// <para>
        /// ⚠️ <b>别再换回 76/77/78</b>：那三张是这张表里<b>另一只</b>暗色乌龟
        /// （主色 <c>#004058</c> 占到 74%，整体看着就是"墨绿、几乎只剩描边"），
        /// 而绿龟的配色是 <c>#E45C10</c>（橙脚）+ <c>#008888</c>（青壳）+ <c>#F0D0B0</c>（白脸）——
        /// 与参考截图 <c>_assets_tmp/SMB-clone/Screenshots/world1-2.jpg</c> 量到的主色
        /// （<c>#008888</c> 系为主、深色仅作描边）一致。
        /// <b>上一次的问题不是"动画不对"，而是选错了乌龟版本。</b>
        /// </para>
        /// <para>
        /// 38/40 的选择依据是<b>像素级两两差异</b>：这一对差异最小（43 个像素，差的正是四条腿），
        /// 即同一姿势的相邻帧；其余几张与这对都差 130 以上（<c>#36</c> 差 240+，是翻转 / 死亡那类）。
        /// 走路动画必须<b>换图</b>，不能靠 <c>flipX</c> 左右翻（那样会原地"打转"）；朝向仍由 <c>flipX</c> 负责。
        /// </para>
        /// </summary>
        public const string KoopaWalk = "smb_enemies_sheet_38";
        public const string KoopaWalk2 = "smb_enemies_sheet_40";
        /// <summary>
        /// 乌龟缩进壳里（静止与被踢滑行都用它）。
        /// <para>
        /// <c>smb_enemies_sheet_42</c>：18x16、<b>以 <c>#008888</c> 青绿为主</b>（86 个像素），
        /// 与参考截图里乌龟的主色一致（原先指向的 <c>#78</c> 主色是深色 <c>#004058</c>，是另一只）。
        /// 它和 41 属于同一形状家族（两两差异仅 38）：41 底部多两只脚，42 是干净的圆壳，故取 42。
        /// </para>
        /// </summary>
        public const string KoopaShell = "smb_enemies_sheet_42";
        /// <summary>食人花（青色，地下关配色；两帧交替表现张嘴）。</summary>
        public const string Piranha0 = "smb_enemies_sheet_43";
        public const string Piranha1 = "smb_enemies_sheet_44";

        /// <summary>
        /// 地下关的砖（青色）。1-2 的墙、地面、移动平台都用它。
        /// <para>关卡数据里也是写这个名字（`T x y 0 WorldTileSprites_1`），这里给代码侧一个单一来源。</para>
        /// </summary>
        public const string TileUndergroundBrick = "WorldTileSprites_1";

        /// <summary>火球两帧。</summary>
        public static readonly string[] Fireball =
        {
            "smb_enemies_sheet_82", "smb_enemies_sheet_83",
        };

        /// <summary>火球爆炸帧。</summary>
        public static readonly string[] FireballBoom =
        {
            "smb_enemies_sheet_110", "smb_enemies_sheet_111", "smb_enemies_sheet_112",
        };

        /// <summary>
        /// 旗杆：杆身 / 顶球 / 旗子。
        /// <para>
        /// ⚠️ **这三张图的编号是逐像素量出来的，不是按网格猜的** —— 原表
        /// <c>FlagpoleSprites.png</c>（80×32）的版式是「<b>上排：旗 + 球；下排：旗 + 杆</b>」，
        /// 也就是**一个球和它自己的杆不在同一 16 像素行里**，所以按 16×16 网格行优先硬切
        /// 必然把图形切坏：
        /// </para>
        /// <list type="bullet">
        /// <item><c>_0</c>(x0..15,y0..15) = **白旗**（斜三角）</item>
        /// <item><c>_1</c>(x16..31,y0..15) = **绿球**（顶球；x21..28 / y8..15）</item>
        /// <item><c>_6</c>(x16..31,y16..31) = **绿杆**（2px 宽，x23..24，向下贯穿整格）</item>
        /// </list>
        /// <para>
        /// <b>踩过的坑</b>：这里原先写的是 <c>_0</c> 当杆身、<c>_2</c> 当旗子 ——
        /// 于是那片"斜切白旗"被纵向拉伸 9 倍（玩家看到的就是"一张被拉得很高的图"），
        /// 而"旗子"位置站着的是 <c>_2</c> 那个银球。**素材表一定要看像素，不能按网格推。**
        /// </para>
        /// </summary>
        public const string FlagpolePole = "FlagpoleSprites_6";
        public const string FlagpoleTop = "FlagpoleSprites_1";
        public const string Flag = "FlagpoleSprites_0";

        /// <summary>
        /// 升降台的台面（3 格宽 × 半格厚）。
        /// <para>出处 = clone `Prefabs/Platforms/Moving Platform Vertical.prefab:222` 的 `m_Sprite`
        /// （`fileID: 21300014` ⇒ `Assets/Sprites/misc-3.gif` 的 `moving_platform_6`，
        /// `.meta` 里它的 `rect = x:143, y:1201, width:48, height:8` ⇒ 48×8 px = 3×0.5 格，
        /// 与同一 prefab `:121` 的 `m_Size: {x: 3, y: 0.5}` 一致）。
        /// 裁切脚本 `原版资源/解析/脚本/gen_platform_sprite.py`（可复跑、带自检）。</para>
        /// </summary>
        public const string MovingPlatform = "MovingPlatform";

        /// <summary>城堡（整张图，10x11 格）。</summary>
        public const string Castle = "Castle_0";

        /// <summary>
        /// 标题画面的原版 logo。
        /// <para>
        /// 出处：原版素材包的标题画面图表（`title.png`，Ripped By LH206）里的 "Title Logo" ——
        /// 淡粉字 + 黑描边、放在橙色圆角底板上，四周透明，可直接当一张图用（不需要抠色）。
        /// 裁切矩形 x=4,y=58,174x90（对着图表量出来的）。
        /// </para>
        /// </summary>
        public const string TitleLogo = "TitleLogo";
    }

    /// <summary>
    /// 马里奥精灵文件名（= <c>Resources/Sprites/Mario/</c> 下的文件名，不含扩展名）。
    /// <para>
    /// 这些名字来自原版动画剪辑的解析结果（<c>smb_mario_sheet_7</c> = 小马里奥站立，等等），
    /// 所以这里的每一项都能追溯到原版的某一帧，不是自己编的动作划分。
    /// </para>
    /// </summary>
    public static class MarioAction
    {
        public const string SmallIdle = "Small_Idle";
        public const string SmallRun0 = "Small_Run0";
        public const string SmallRun1 = "Small_Run1";
        public const string SmallRun2 = "Small_Run2";
        public const string SmallJump = "Small_Jump";
        public const string SmallSkid = "Small_Skid";
        public const string SmallClimb0 = "Small_Climb0";
        public const string SmallClimb1 = "Small_Climb1";

        public const string BigIdle = "Big_Idle";
        public const string BigRun0 = "Big_Run0";
        public const string BigRun1 = "Big_Run1";
        public const string BigRun2 = "Big_Run2";
        public const string BigJump = "Big_Jump";
        public const string BigSkid = "Big_Skid";
        public const string BigCrouch = "Big_Crouch";
        public const string BigClimb0 = "Big_Climb0";
        public const string BigClimb1 = "Big_Climb1";

        public const string FireIdle = "Fire_Idle";
        public const string FireRun0 = "Fire_Run0";
        public const string FireRun1 = "Fire_Run1";
        public const string FireRun2 = "Fire_Run2";
        public const string FireJump = "Fire_Jump";
        public const string FireSkid = "Fire_Skid";
        /// <summary>
        /// 火马里奥蹲下。
        /// <para>
        /// ⚠️ <b>这张切片原来是错的，别再切回去</b>：原文件是一张 <b>18x32 的站立火马里奥</b>
        /// （和 <c>Fire_Idle</c> 一样高、轮廓也直立），于是"火马里奥按 ↓ 蹲下"在画面上
        /// <b>毫无变化</b>（碰撞盒其实变小了，但贴图还是站着的）—— 所以它看起来像"下蹲功能没做"。
        /// </para>
        /// <para>
        /// 已按原版的机制重新生成：火马里奥整套本来就是<b>大马里奥的调色板替换</b>
        /// （<c>Big_Idle</c> 与 <c>Fire_Idle</c> 的三种颜色一一对应：
        /// <c>#AC7C00→#F83800</c>、<c>#F83800→#FFE0A8</c>、<c>#FFA440→#FFA044</c>），
        /// 所以取 <c>Big_Crouch</c>（17x24、内容高 22 的蹲姿）套上火配色即可。
        /// 现在的文件是 17x24、不透明 275 像素，颜色只有 <c>#F83800 / #FFE0A8 / #FFA044</c>。
        /// </para>
        /// </summary>
        public const string FireCrouch = "Fire_Crouch";
        public const string FireClimb0 = "Fire_Climb0";
        public const string FireClimb1 = "Fire_Climb1";

        public const string Dead = "Dead";
        public const string Grow0 = "Grow0";
        public const string Grow1 = "Grow1";
        public const string FireTrans0 = "FireTrans0";
        public const string FireTrans1 = "FireTrans1";
    }

    /// <summary>
    /// 音效文件名（= <c>Resources/Sound/SFX/</c> 下的文件名，不含扩展名）。
    /// 名字与原版素材包一致，便于对照。
    /// </summary>
    public static class Sfx
    {
        public const string Jump = "jump";
        public const string JumpSmall = "jumpsmall";
        public const string Coin = "coin";
        public const string Bump = "bump";
        public const string Brick = "brick";
        public const string Break = "kickkill";
        public const string PowerUpAppear = "item";
        public const string PowerUp = "powerup";
        public const string Pipe = "pipepowerdown";

        /// <summary>
        /// 掉能力（受伤缩小）音效。
        /// <para>
        /// **与"进管"共用同一个采样**，不是另挑一个音 —— 出处是素材包自己的**文件名**
        /// <c>Resources/Sound/SFX/pipepowerdown.wav</c>：原版素材把它命名成 "pipe" + "powerdown"
        /// 一个名字，即"进管 / 掉能力"两用（原版马里奥受伤降级与钻进水管响的是同一个音）。
        /// 本工程此前只把它接给了进管（<see cref="Pipe"/>），受伤那条路一声不响 ——
        /// 用户 2026-09-20 点名「受伤没有音效」。
        /// </para>
        /// </summary>
        public const string PowerDown = Pipe;

        public const string Fire = "fire";
        public const string Fireball = "fireball";
        public const string Stomp = "stompswim";
        public const string Death = "death";
        public const string OneUp = "1up";
        public const string Flagpole = "flagpole";
        public const string LevelComplete = "LevelComplete";
        public const string GameOver = "gameover";
        public const string Pause = "pause";
        public const string Beep = "beep";
        public const string HurryUp = "hurryup";
    }

    /// <summary>
    /// BGM 文件名（= <c>Resources/Sound/BGM/</c> 下的文件名）。
    /// <para>两条都来自原版素材包（clone `Assets/Sounds/*.mp3`，按 §1.9 复制进工程）：
    /// `01-main-theme-overworld.mp3`（2970208 字节）与 `02-underworld.mp3`（1192588 字节，
    /// 出处 `原版资源/参考工程/SMB-clone/Assets/Sounds/02-underworld.mp3`）。</para>
    /// </summary>
    public static class Bgm
    {
        public const string Overworld = "01-main-theme-overworld";

        /// <summary>
        /// 地下主题。**哪个场景放哪首由 clone 的场景引用决定**（不是我们选的）：
        /// `World 1-1.unity` / `World 1-2 - Castle Cut.unity` → overworld；
        /// `World 1-2.unity` / `World 1-1 - Underground.unity` / `World 1-2 - Underground.unity` → underworld。
        /// 逐场景引用核对法见 `原版资源/清单.md`（按 mp3 的 guid 在 .unity 里检索）。
        /// </summary>
        public const string Underworld = "02-underworld";
    }
}
