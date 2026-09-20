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

            // ★ 看门狗：这几项里只要有一个回调不来，流程就会【永久】卡在 Loading ——
            // 而且全程没有任何报错（黑屏 + 读条屏不消失），极难定位。
            //
            // 为什么会不回调：资源模块对"路径不存在"的加载【不会调用回调】（不是回 null）。
            // 实测踩过一次：城堡误用 ResPaths.Flagpole() 去 Sprites/Flagpole/ 里找
            // Castle_0，文件不存在，pending 永远停在 1，流程死锁在 Loading。
            //
            // 与其让它安静地卡住，不如超时后报一条明确的 Error 并强行推进 ——
            // 让"资源路径写错"以它本来的面目暴露出来。
            Game.Timer.After(5f, () =>
            {
                if (finished) return;
                Game.Logger.Error("Level",
                    $"关卡装饰加载超时（还差 {pending} 项未回调），强制推进流程；" +
                    "多半是资源路径写错 —— 检查 ResPaths 下的目录与 SpriteNames 是否对得上");
                finished = true;
                onDone?.Invoke();
            });

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
                Done();
            });

            // 旗子：挂在杆上、初始靠近顶端；马里奥抓住杆下滑时由 PlayerActor 带着一起往下降。
            // 踩过的坑：这里原先【只建了杆身和顶球，没建旗子】—— 原版旗杆上是有一面旗的，
            // 少了它就是"杆子光秃秃"（而且定义好的 SpriteNames.Flag 没人用，属于写了没接）。
            //
            // ★ 建好后【登记到 StageContext】而不是让 PlayerActor 按名字 Find（历史债 E-5）：
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
            // 那条路径下没有 Castle_0，加载回调不会来（见上面的看门狗注释）。
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
