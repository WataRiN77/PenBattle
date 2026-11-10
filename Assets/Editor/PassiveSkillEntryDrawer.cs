using UnityEditor;
using UnityEngine;

// 让 PenConfig 里的被动条目在 Inspector 里显示成 {效果 / 方式 / 有意义的数值名}
// 例如选中「冲击波」时，两个数值会显示为「推力」和「半径（米）」
// 只有"参数修改型"被动（坚硬/顺滑）才显示「方式」下拉，其余被动的 value 是玩法参数，加/乘无意义
[CustomPropertyDrawer(typeof(PassiveSkillEntry))]
public class PassiveSkillEntryDrawer : PropertyDrawer
{
    private const float Gap = 2f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var e = GetEffect(property);
        float line = EditorGUIUtility.singleLineHeight;
        float h = line * 2f + Gap;                                   // 效果 + 主数值
        if (PassiveSkillInfo.SupportsOp(e)) h += line + Gap;          // 方式
        if (PassiveSkillInfo.GetValue2Label(e) != null) h += line + Gap;  // 副数值
        return h;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var effectProp = property.FindPropertyRelative("effect");
        var operationTypeProp     = property.FindPropertyRelative("operationType");
        var valueProp  = property.FindPropertyRelative("value");
        var value2Prop = property.FindPropertyRelative("value2");

        var e = GetEffect(property);
        var operationType = (ModifierOp)operationTypeProp.intValue;
        float line = EditorGUIUtility.singleLineHeight;

        var r = new Rect(position.x, position.y, position.width, line);
        EditorGUI.PropertyField(r, effectProp, new GUIContent("效果"));

        if (PassiveSkillInfo.SupportsOp(e))
        {
            r.y += line + Gap;
            int operationTypeIndex = EditorGUI.Popup(r, "方式", operationTypeProp.intValue, ModifierOpInfo.DisplayNames);
            if (operationTypeIndex != operationTypeProp.intValue)
            {
                operationTypeProp.intValue = operationTypeIndex;
                operationType = (ModifierOp)operationTypeIndex;
            }
        }

        r.y += line + Gap;
        EditorGUI.PropertyField(r, valueProp,
            new GUIContent(PassiveSkillInfo.GetValueLabel(e, operationType) ?? "主数值"));

        string label2 = PassiveSkillInfo.GetValue2Label(e);
        if (label2 != null)
        {
            r.y += line + Gap;
            EditorGUI.PropertyField(r, value2Prop, new GUIContent(label2));
        }
    }

    private static PassiveEffectType GetEffect(SerializedProperty property)
    {
        var effectProp = property.FindPropertyRelative("effect");
        return (PassiveEffectType)effectProp.enumValueIndex;
    }
}
