using System;
using CloverEngine;
using SuperMario.Core;
using SuperMario.Module.Flow;
using UnityEngine;

namespace SuperMario.Module.Level
{
    /// <summary>
    /// 关卡的固定装饰：旗杆、旗子、城堡。
    /// <para>
    /// 这三样不是瓦片也不是关卡文件里的实体 —— 它们的位置由**关卡本身**决定（终点在哪），
    /// 而且每关必然各有一个。所以按"关卡属性"处理，位置由 <see cref="ILevel"/> 给出，
    /// 而不是混进瓦片表让美术在关卡数据里手动摆。
    /// </para>
    /// 数值取自原版场景里这三个对象的世界坐标（旗杆 x=184.5、城堡 x=190.5），
    /// 高度按原版素材比例摆放。
    /// </summary>
    internal static class LevelProps
    {
        /// <summary>旗杆高度（格）。原版杆身 9 格 + 顶球 1 格。</summary>
        private const int PoleHeight = 9;

        public static void Build(ILevel level, Transform parent, Action onDone)
        {
            // 池化取舍：本文件的 5 处 `new GameObject`（Props 根 / 杆身 / 顶球 / 旗子 / 城堡）
            //   各 1 个、生命周期 = 一整关，**不进对象池** —— 池化只在"一局里反复生成销毁"时才划算。
            var root = new GameObject("Props").transform;
            root.SetParent(parent, false);

            var poleX = level.FlagpoleX;
            var groundY = level.GroundTopY;

            // 本关没有旗杆/城堡（1-2 地下段：原版把旗杆放在后面的地表段里）：
            // 一件都不建，直接回调。否则会往关卡里塞一根【插在墙里】的旗杆，而且
            // 玩家侧的通关判定会拿它当终点 —— 表现就是过关时人走进墙里再掉出世界。
            if (!level.HasFlagpole)
            {
                Game.Logger.Info("Level", "本关没有旗杆/城堡（关卡数据未声明旗杆）→ 跳过 Props");
                onDone?.Invoke();
                return;
            }

            var pending = 4;
            var finished = false;
            void Done()
            {
                if (--pending <= 0 && !finished)
                {
                    finished = true;
                    onDone?.Invoke();
                }
            }

            //
            // 旧前提（原注释原文）："资源模块对'路径不存在'的加载【不会调用回调】（不是回 null）"。
            // 回读引擎实现后确认与契约不符：
            //   · `Runtime/Core/Contracts.cs:1033`（以及 :1042 带进度版）明写
            //     "<c>callback</c>：加载完成回调，**加载失败时资源参数为 null**"；
            //   · `Runtime/Resource/ResourceManager.cs:305-343` 的 `CompletePending` 在
            //     `asset == null` 时先打一条 "加载失败：{path}"（:326），**然后照样把全部回调
            //     逐个 invoke 一遍**（:329-339，参数就是 null）；
            //   · `Runtime/Resource/ResourceBackend.cs:233-259` 的 Resources 后端：路径非法时
            //     自己 `onDone?.Invoke(null)` 后就返回（:242），正常路径走 `req.completed` 事件
            //     （`:258`，文件不存在时该事件照样触发、`req.asset` 就是 null）；
            //     `ResourceManager.cs:217-221` 还给 `BeginLoad` 整个包了一层 catch ⇒ 抛异常也是
            //     `CompletePending(pending, null)`。
            //   ⇒ **回调必到**（成功给对象、失败给 null）。所以"5 秒后抢跑"永远只会在
            //     一次正常加载里插一条假 Error（实测代价：把 `[Error] = 0` 的验收判据污染掉）。
            //
            // 现在的口径：**"资源缺失"由每个回调自己判 `sp == null` 并报 Error**（下面四处都有），
            // 而"回调真的没来"（引擎级故障）由 `AppFlow` 那条绑定 Loading token 的
            // `AfterUnscaled` 看门狗兜 —— 判据是"`fsm` 仍是 Loading 且 token 未变"，
            // 也就是"确实还有回调没到"，而不是"路径不存在"。两条判据各判各的事，不再互相冒充。

            // 杆身：用一张 1 格宽的竖图纵向拉伸到 PoleHeight 格。
            Game.Res.LoadAsset<Sprite>(ResPaths.Flagpole(SpriteNames.FlagpolePole), sp =>
            {
                var go = new GameObject("Flagpole");
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(poleX, groundY + PoleHeight * 0.5f, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sortingOrder = 2;
                if (sp != null)
                {
                    // 竖向拉伸：sprite 原始是 1 格高，拉成 PoleHeight 格。
                    var h = sp.bounds.size.y;
                    if (h > 0.001f) go.transform.localScale = new Vector3(1f, PoleHeight / h, 1f);
                }
                else
                {
                    Game.Logger.Error("Level", $"旗杆杆身贴图缺失:{SpriteNames.FlagpolePole}");
                }
                Done();
            });

            Game.Res.LoadAsset<Sprite>(ResPaths.Flagpole(SpriteNames.FlagpoleTop), sp =>
            {
                var go = new GameObject("FlagpoleTop");
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(poleX, groundY + PoleHeight + 0.5f, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sortingOrder = 2;
                // 与其余三处同口径：失败回调必到（参数为 null），所以缺失只能在这里报（见上面的修正说明）。
                if (sp == null) Game.Logger.Error("Level", $"旗杆顶球贴图缺失:{SpriteNames.FlagpoleTop}");
                Done();
            });

            // 旗子：挂在杆上、初始靠近顶端；马里奥抓住杆下滑时由 PlayerActor 带着一起往下降。
            // 少了它就是"杆子光秃秃"（而且定义好的 SpriteNames.Flag 没人用，属于写了没接）。
            //
            // 建好后【登记到 StageContext】而不是让 PlayerActor 按名字 Find（历史债 E-5）：
            // 谁建谁登记，名字就不再是契约。登记点在加载回调里 —— 若这里没跑到，宁可让
            // PlayerActor 在滑杆时打一条 Warn（明确暴露），也不要"按名字找得到就悄悄对、找不到就悄悄错"。
            Game.Res.LoadAsset<Sprite>(ResPaths.Flagpole(SpriteNames.Flag), sp =>
            {
                var go = new GameObject("Flag");
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(poleX - 0.35f, groundY + PoleHeight - 0.6f, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sortingOrder = 2;
                if (sp == null) Game.Logger.Error("Level", $"旗子贴图缺失:{SpriteNames.Flag}");
                StageContext.SetFlag(go.transform);
                Done();
            });

            // 城堡：整张图（10x11 格），底部对齐地面。
            // 注意目录：城堡在 Sprites/Castle/ 下，不要写成 ResPaths.Flagpole() ——
            // 那条路径下没有 Castle_0，回调会带着 **null** 到（不是"不来"，见上面的修正说明），
            // 于是下面那条 `sp == null` 的 Error 会直接点名是哪个常量写错了。
            Game.Res.LoadAsset<Sprite>(ResPaths.Castle(SpriteNames.Castle), sp =>
            {
                var go = new GameObject("Castle");
                go.transform.SetParent(root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sortingOrder = 1;
                if (sp != null)
                {
                    // 城堡按图片原始尺寸（格）摆放，底部贴地面。
                    // x 用 level.CastleDoorX：它同时是"走进城堡"的终点，两边必须是同一个数。
                    var h = sp.bounds.size.y;
                    go.transform.position = new Vector3(level.CastleDoorX, groundY + h * 0.5f, 0f);
                }
                else
                {
                    Game.Logger.Error("Level", $"城堡贴图缺失:{SpriteNames.Castle}");
                }
                Done();
            });
        }
    }
}
