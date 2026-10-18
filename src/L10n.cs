namespace RfcEditor;

public static class L10n
{
    public static bool English { get; set; }
    public static string T(string key, params object[] args)
    {
        var pair = Strings[key];
        return string.Format(English ? pair.En : pair.Zh, args);
    }
    public static readonly Dictionary<string,(string Zh,string En)> Strings = new()
    {
        ["app"]=("RFC 武器参数编辑器", "RFC Weapon Editor"),
        ["open"]=("打开 .rfc", "Open .rfc"), ["save"]=("保存", "Save"), ["saveAs"]=("另存 RFC", "Save As"),
        ["reset"]=("还原当前组件", "Reset component"), ["report"]=("解析报告", "Parse report"), ["all"]=("全部组件", "All components"),
        ["componentSearch"]=("搜索武器或子弹名称", "Search weapon or projectile names"),
        ["fieldSearch"]=("搜索字段：伤害 / damage / 弹匣 / ammo …", "Search fields: damage / ammo / speed …"),
        ["references"]=("显示资源引用", "Show asset references"),
        ["label"]=("说明", "Description"), ["path"]=("字段路径", "Field path"), ["type"]=("类型", "Type"),
        ["value"]=("数值（双击修改）", "Value (double-click to edit)"), ["original"]=("上次保存值", "Last saved value"),
        ["welcome"]=("打开 Ravenfield 武器模组", "Open a Ravenfield weapon mod"),
        ["intro"]=("选择 .rfc 或将文件拖到窗口。Projectile 和 Weapon 会自动分类。", "Select or drop a .rfc file. Projectile and Weapon components are listed automatically."),
        ["noFile"]=("尚未打开文件。", "No file is open."),
        ["reportText"]=("Unity {0}\n已识别 {1} 个可无损编辑的组件。\n\n{2}", "Unity {0}\nFound {1} losslessly editable components.\n\n{2}"),
        ["noWarnings"]=("没有解析警告。", "No parse warnings."),
        ["discard"]=("当前仍有未保存的修改。继续将丢弃这些修改。", "There are unsaved changes. Continuing will discard them."),
        ["discardTitle"]=("关闭当前编辑", "Unsaved changes"),
        ["parsing"]=("正在解析资源包…", "Reading asset bundle…"),
        ["noComponents"]=("未找到可无损编辑的 Projectile / Weapon。\n请查看解析报告。缺少类型树的模组暂不支持。", "No losslessly editable Projectile / Weapon components were found.\nSee the parse report. Mods without full type trees are not supported yet."),
        ["parseResult"]=("解析结果", "Parse result"), ["openFailed"]=("打开失败", "Open failed"),
        ["noMatch"]=("没有匹配的组件", "No matching components"),
        ["adjustFilter"]=("调整左侧筛选，或查看解析报告。", "Adjust the component filter or check the parse report."),
        ["invalid"]=("输入无效：{0}", "Invalid value: {0}"),
        ["fixInput"]=("输入无效，请修正，或按 Esc 取消该单元格修改。", "Invalid value. Correct it, or press Esc to cancel the cell edit."),
        ["saving"]=("正在保存并逐项校验资源…", "Saving and verifying all assets…"),
        ["saved"]=("保存完成", "Saved"), ["saveFailed"]=("保存失败", "Save failed"),
        ["savedInPlace"]=("已保存并校验：\n{0}\n\n自动备份：\n{1}", "Saved and verified:\n{0}\n\nAutomatic backup:\n{1}"),
        ["savedAs"]=("已另存并校验：\n{0}\n\n现在编辑此新文件，后续 Ctrl+S 将保存到此文件。", "Saved and verified:\n{0}\n\nYou are now editing this new file. Ctrl+S will save to this file."),
        ["ready"]=("就绪 · 支持保留类型树的 UnityFS .rfc 模组", "Ready · Supports UnityFS .rfc mods with full type trees"),
        ["status"]=("Unity {0} · {1} 个组件 · {2} 个字段未保存 · Ctrl+S 保存并自动备份", "Unity {0} · {1} components · {2} unsaved fields · Ctrl+S saves with a backup"),
        ["filter"]=("Ravenfield 模组 (*.rfc)|*.rfc|所有文件 (*.*)|*.*", "Ravenfield mods (*.rfc)|*.rfc|All files (*.*)|*.*"),
        ["rfcFilter"]=("Ravenfield 模组 (*.rfc)|*.rfc", "Ravenfield mods (*.rfc)|*.rfc"),
        ["readonly"]=("资源引用仅供查看。", "Asset references are read-only."),
        ["unsupportedType"]=("不支持此字段类型。", "This field type is not supported."),
        ["finite"]=("请输入有限数值。", "Enter a finite number."),
        ["invalidNumber"]=("请输入符合字段类型和范围的数值；小数请使用英文句点。", "Enter a value within the field's type and range. Use a dot for decimals."),
        ["roundtrip"]=("{0} 不能无损往返序列化，已跳过。", "{0} cannot be serialized losslessly and was skipped."),
        ["noFields"]=("缺少可编辑字段或类型树。", "Missing editable fields or type tree."), ["unnamed"]=("未命名", "Unnamed"),
        ["noProjectile"]=("未找到子弹引用", "No projectile reference found"),
        ["usesProjectile"]=("使用子弹：{0}", "Uses projectile: {0}"),
        ["projectileRef"]=("子弹引用：{0} / {1}（未在当前列表解析）", "Projectile reference: {0} / {1} (not resolved in this list)"),
        ["shared"]=("共用此子弹的武器：{0}", "Weapons sharing this projectile: {0}"),
        ["noUsers"]=("未找到当前包内的武器引用", "No weapon references found in this bundle"),
        ["newName"]=("另存时请选择新文件名。覆盖当前文件请使用“保存”。", "Choose a new filename for Save As. Use Save to overwrite the current file."),
        ["exists"]=("目标已存在，请选择新文件名。", "The destination exists. Choose a new filename."),
        ["directoryChanged"]=("保存校验失败：包内目录变化。", "Save verification failed: bundle directory changed."),
        ["idsChanged"]=("保存校验失败：资源 ID 变化。", "Save verification failed: asset IDs changed."),
        ["dataMismatch"]=("保存校验失败：资源 {0} 数据不匹配。", "Save verification failed: data mismatch in asset {0}."),
        ["streamsChanged"]=("保存校验失败：附属资源变化。", "Save verification failed: resource stream changed."),
        ["externalChange"]=("当前文件已被其他程序修改，已取消覆盖。请先另存新文件，或重新打开当前文件。", "The current file was changed by another program. Save was cancelled. Save As to a new file, or reopen the current file."),
    };
    public static readonly Dictionary<string,(string Zh,string En)> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["damage"]=("子弹伤害","Bullet damage"),["speed"]=("弹速","Projectile speed"),["balanceDamage"]=("失衡伤害","Balance damage"),["impactForce"]=("撞击力","Impact force"),
        ["lifetime"]=("子弹存活时间","Projectile lifetime"),["gravityMultiplier"]=("重力倍率","Gravity multiplier"),["dropoffEnd"]=("伤害衰减终点","Damage drop-off end"),["armorDamage"]=("装甲伤害等级（枚举）","Armor damage rating (enum)"),
        ["ammo"]=("弹匣容量","Magazine capacity"),["spareAmmo"]=("备用弹药","Spare ammo"),["reloadTime"]=("换弹时间","Reload time"),["cooldown"]=("开火间隔（秒）","Shot interval (seconds)"),["auto"]=("自动射击（0/1）","Automatic fire (0/1)"),
        ["projectilesPerShot"]=("每发弹丸数","Projectiles per shot"),["kickback"]=("后坐力","Kickback"),["randomKick"]=("随机后坐力","Random kick"),["spread"]=("基础散布","Base spread"),["aimFov"]=("瞄准视野","Aim field of view"),
        ["effectiveRange"]=("AI 有效射程","AI effective range"),["resupplyNumber"]=("补给弹药数","Resupply amount"),["maxAmmoPerReload"]=("单次换弹上限","Max ammo per reload"),["loud"]=("响亮枪声（0/1）","Loud weapon (0/1)"),
        ["piercing"]=("穿透（0/1）","Piercing (0/1)"),["autoAssignArmorDamage"]=("自动设置装甲等级（0/1）","Auto armor rating (0/1)"),["projectilePrefab"]=("子弹对象引用","Projectile prefab reference"),
        ["unholsterTime"]=("切出武器时间","Unholster time"),["aimSensitivityMultiplier"]=("瞄准灵敏度倍率","Aim sensitivity multiplier")
    };
    public static string Label(string field) => Labels.TryGetValue(field,out var p) ? (English?p.En:p.Zh) : "";
}
