// 这个文件已经被拆开，不再包含任何类型 —— 保留它只是为了留下搬迁说明。
//
// 原内容：BootPanel / MainMenuPanel / CharSelectPanel 三个 MonoBehaviour 挤在一个 .cs 里。
//
// 为什么必须拆：
//   Unity 对"一个 .cs 里有多个 MonoBehaviour"只会给其中一个分配 fileID 11500000，
//   其余类型的引用 ID 是另一套值。而预制体里的 m_Script 是 (guid, fileID) 二元组 ——
//   我按 11500000 写出去的引用，对非首个类就是错的。症状是：
//     预制体文件看起来"有组件"、m_Name 也对，但运行时 UIManager 报
//     "Component X not found on prefab"，面板永远打不开。
//   而且这个症状不会在编译期暴露，只在运行时静默失败。
//
// 现在的分布：
//   BootPanel        -> Assets/Scripts/UI/BootPanel.cs
//   MainMenuPanel    -> Assets/Scripts/UI/MainMenuPanel.cs
//   CharSelectPanel  -> Assets/Scripts/UI/CharSelectPanel.cs
//
// 规矩：一个文件一个 MonoBehaviour。见 经验.md §4.7。
