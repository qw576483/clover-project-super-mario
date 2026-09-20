// 冒烟探针：确认 eval_file 能访问本工程程序集 + 能写日志（全限定名）。
CloverEngine.Game.Logger.Info("Probe", "PROBE-SMOKE 开始");
var lvl = SuperMario.Module.Flow.StageContext.Level;
CloverEngine.Game.Logger.Info("Probe", $"PROBE-SMOKE StageContext.Level={(lvl == null ? "null" : lvl.GetType().Name)} 输入后端={(CloverEngine.Game.Input == null ? "null" : CloverEngine.Game.Input.BackendName)}");
CloverEngine.Game.Logger.Info("Probe", "PROBE-SMOKE-DONE");
