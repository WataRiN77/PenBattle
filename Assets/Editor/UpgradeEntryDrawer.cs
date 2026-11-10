using UnityEditor;
using UnityEngine;

// 让强化条目在 Inspector 里显示成有意义的名字，并把"乘算/加算"摆在数值前面
// 例如：效果=质量强化、方式=乘算 时，数值字段显示为「质量加成比例（0.5 = +50%）」
//       效果=质量强化、方式=加算 时，数值字段显示为「质量加算（2 = +2 点）」
// 效果=Passive（行为型）时不再显示「方式 / 数值」，改为内嵌一条完整的被动配置，
// 直接复用 PassiveSkillEntryDrawer 的绘制逻辑，参数含义（层数/推力/半径…）自动跟着变。
[CustomPropertyDrawer(typeof(UpgradeEntry))]
public class UpgradeEntryDrawer : PropertyDrawer
{
    private const float Gap = 2f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float line = EditorGUIUtility.singleLineHeight;

        if (IsBehavior(property))
        {
            var passiveProp = property.FindPropertyRelative("passive");
            return line + Gap + EditorGUI.GetPropertyHeight(passiveProp, GUIContent.none, true);
        }

        return line * 3f + Gap * 2f;   // 效果 + 方式 + 数值
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var effectProp = property.FindPropertyRelative("effect");
        var operationTypeProp = property.FindPropertyRelative("operationType");
        var valueProp = property.FindPropertyRelative("value");
        var passiveProp = property.FindPropertyRelative("passive");

        var e = (UpgradeEffectType)effectProp.enumValueIndex;
        var operationType = (ModifierOp)operationTypeProp.intValue;
        float line = EditorGUIUtility.singleLineHeight;

        var r = new Rect(position.x, position.y, position.width, line);
        EditorGUI.PropertyField(r, effectProp, new GUIContent("效果"));

        // 行为型强化：内嵌被动配置
        if (e == UpgradeEffectType.Passive)
        {
            r.y += line + Gap;
            r.height = EditorGUI.GetPropertyHeight(passiveProp, GUIContent.none, true);
            EditorGUI.PropertyField(r, passiveProp, GUIContent.none, true);
            return;
        }

        r.y += line + Gap;
        int operationTypeIndex = EditorGUI.Popup(r, "方式", operationTypeProp.intValue, ModifierOpInfo.DisplayNames);
        if (operationTypeIndex != operationTypeProp.intValue)
        {
            operationTypeProp.intValue = operationTypeIndex;
            operationType = (ModifierOp)operationTypeIndex;   // 同帧更新，下面那行标签立刻跟着变
        }

        r.y += line + Gap;
        EditorGUI.PropertyField(r, valueProp, new GUIContent(UpgradeInfo.GetValueLabel(e, operationType)));
    }

    private static bool IsBehavior(SerializedProperty property)
    {
        var effectProp = property.FindPropertyRelative("effect");
        return (UpgradeEffectType)effectProp.enumValueIndex == UpgradeEffectType.Passive;
    }
}
