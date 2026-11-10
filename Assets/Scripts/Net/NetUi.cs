using UnityEngine;

/// IMGUI 面板的统一缩放工具。
///
/// 为什么需要：Unity 自带的 IMGUI 皮肤字号是按 1080p 设计的，在高分屏 / 缩放显示器上
/// 看起来又小又挤，调试很费眼。这里用 `GUI.matrix` 整体放大，
/// **控件坐标仍然按"逻辑像素"写**，位置和字号一起放大，不会错位。
///
/// 用法：
///   private void OnGUI()
///   {
///       NetUi.Begin();
///       GUILayout.BeginArea(new Rect(20f, 20f, 380f, 300f), GUI.skin.box);   // 逻辑坐标
///       ...
///       GUILayout.EndArea();
///       NetUi.End();
///   }
/// 居中时请用 NetUi.LogicalWidth / LogicalHeight（不要直接用 Screen.width）。
public static class NetUi
{
    /// <summary>整体缩放倍数。嫌小就往上调（1.5 ~ 2.5 之间）。</summary>
    public const float Scale = 1.7f;

    public static float LogicalWidth => Screen.width / Scale;
    public static float LogicalHeight => Screen.height / Scale;

    private static Matrix4x4 _prev;

    /// <summary>进入放大坐标系（OnGUI 开头调用）</summary>
    public static void Begin()
    {
        _prev = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                                   new Vector3(Scale, Scale, 1f));
    }

    /// <summary>恢复原坐标系（OnGUI 结尾调用，务必与 Begin 成对）</summary>
    public static void End()
    {
        GUI.matrix = _prev;
    }
}
