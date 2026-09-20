// 这个文件已经被拆开，不再包含任何类型 —— 保留它只是为了留下搬迁说明。
//
// 原内容：LoadingPanel / HudPanel / PausePanel / ResultPanel / GameOverPanel 挤在一个 .cs 里。
// 拆分理由与 MenuPanels.cs 相同（一个文件里多个 MonoBehaviour 会导致 m_Script 引用写错）。
//
// 现在的分布：
//   LoadingPanel   -> Assets/Scripts/UI/LoadingPanel.cs
//   HudPanel       -> Assets/Scripts/UI/HudPanel.cs
//   PausePanel     -> Assets/Scripts/UI/PausePanel.cs
//   ResultPanel    -> Assets/Scripts/UI/ResultPanel.cs
//   GameOverPanel  -> Assets/Scripts/UI/GameOverPanel.cs
//
// 规矩：一个文件一个 MonoBehaviour。见 经验.md §4.7。
