using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// первый числовой проход баланса: меняем только урон, перезарядки и соответствующие описания.
public static class SpellBalancePass
{
    private const string BackupFolder = "Logs/SpellBalanceV1";
    private sealed class Rule
    {
        public string id, reason;
        public int beforeDamage, damage;
        public float beforeCooldown, cooldown;
        public Rule(string id, int beforeDamage, int damage, float beforeCooldown, float cooldown, string reason)
        { this.id = id; this.beforeDamage = beforeDamage; this.damage = damage; this.beforeCooldown = beforeCooldown; this.cooldown = cooldown; this.reason = reason; }
    }

    private static readonly Rule[] Rules =
    {
        new Rule("FireBall",20,24,5,4,"чистый одиночный урон без контроля должен окупать попадание; раньше уступал воде и льду"),
        new Rule("WindFlow",0,0,5,5,"силовой толчок 30 уже полезен для разрыва дистанции; урон ему не нужен"),
        new Rule("IceShard",22,22,4,4,"основной урон наборов льда с малым числом атак; замедление важно для их выживания"),
        new Rule("WaterBolt",18,18,3,3,"слабый отдельный удар компенсируется частотой; сохраняем основной урон узких наборов воды"),
        new Rule("Boulder",32,30,5,5.5f,"сильный толчок и большой прямой урон усиливали почти любой набор земли"),
        new Rule("BoilingJet",28,26,6,6,"быстрый снаряд с радиусом 2 метра сохраняет роль надёжного площадного попадания"),
        new Rule("Magma",40,34,9,10,"радиус 3 метра, толчок и подброс слишком хорошо дополняли высокий разовый урон"),
        new Rule("Blizzard",4,4,9,12,"при длительности 7,5 секунды нужен промежуток без зоны; урон за тик оставляем"),
        new Rule("Mud",2,2,8,12,"замедление 65% и зона на 7,5 секунды не должны поддерживаться почти непрерывно"),
        new Rule("SteamCloud",5,4,8,10,"снижаем постоянный площадной урон и долю времени, занятую зоной на 6,5 секунды"),
        new Rule("FireTornado",6,5,10,11,"притяжение и повторные подбросы уже дают ценность; уменьшаем добавочный урон"),
        new Rule("Geyser",10,8,8,10,"каждый тик добавляет сильный подброс; снижаем урон и частоту применения"),
        new Rule("FrostNova",14,18,8,8,"небольшое усиление ближней самообороны; для попадания нужно подпустить противника"),
        new Rule("StoneSkin",0,0,24,18,"щит на 50 и 5 секунд слишком редко был доступен; запас щита не увеличиваем"),
        new Rule("SteamDash",0,0,7,7,"мобильность поддерживает слабые наборы воздуха; рывок не даёт неуязвимость"),
        new Rule("StoneWall",0,0,10,10,"временное препятствие без урона уже имеет окно ответа и требования к свободному месту"),
        new Rule("IceMirror",0,0,12,12,"отражает только один снаряд и требует направления; сильный, но ситуативный ответ"),
        new Rule("FireSeal",35,35,12,12,"высокий урон оправдан секундой взведения и малой областью активации"),
        new Rule("GravityWell",0,0,12,12,"сам урона не наносит; ослабление трясины уменьшает силу связки без обесценивания притяжения"),
        new Rule("SnowDecoy",0,0,10,8,"слабая и избегаемая провокация без урона; усиление только набора воздух-лёд-вода"),
        new Rule("SmokeCloud",0,0,14,14,"длительная завеса без урона и замедления; её ценность зависит от карты и видимости")
    };

    // проверяем исходные значения до любых изменений, чтобы не затереть новые ручные настройки.
    [MenuItem("Tools/Wizard War/Apply spell balance v1")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before applying balance.");
        var spells = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab")
            .GetComponentInChildren<SpellManager>(true).Spells.ToArray();
        if (spells.Length != Rules.Length || spells.Any(s => s == null)) throw new InvalidOperationException("Spell catalog changed.");
        foreach (var rule in Rules)
        {
            Spell spell = spells.Single(s => s.name == rule.id);
            if ((Damage(spell) != rule.beforeDamage && Damage(spell) != rule.damage) ||
                (!Mathf.Approximately(spell.Cooldown, rule.beforeCooldown) && !Mathf.Approximately(spell.Cooldown, rule.cooldown)))
                throw new InvalidOperationException("Unexpected balance values: " + rule.id);
        }
        Directory.CreateDirectory(BackupFolder);
        foreach (Spell spell in spells) Backup(AssetDatabase.GetAssetPath(spell));
        var fire = spells.OfType<FireBall>().Single();
        string firePath = AssetDatabase.GetAssetPath(fire.PreviewPrefab);
        Backup(firePath);
        foreach (var rule in Rules)
        {
            Spell spell = spells.Single(s => s.name == rule.id);
            var data = new SerializedObject(spell);
            data.FindProperty("cooldown").floatValue = rule.cooldown;
            if (spell is ElementalSpell || spell is TacticalSpell) data.FindProperty("damage").intValue = rule.damage;
            data.ApplyModifiedPropertiesWithoutUndo();
            // тексты используют реальные длительности, а не устаревшие значения из первоначального генератора.
            data.Update();
            data.FindProperty("description").stringValue = Description(spell, rule.damage);
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        var root = PrefabUtility.LoadPrefabContents(firePath);
        try
        {
            var data = new SerializedObject(root.GetComponent<FireballProjectile>());
            data.FindProperty("fireballDamage").intValue = Rules.Single(r => r.id == "FireBall").damage;
            data.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, firePath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var identity = AssetDatabase.LoadAssetAtPath<GameObject>(firePath).GetComponent<Mirror.NetworkIdentity>();
        if (identity.assetId == 0) throw new InvalidOperationException("Missing Fireball asset id.");
        // mirror восстанавливает идентификатор по guid после загрузки настоящего ассета.
        EditorUtility.SetDirty(identity);
        PrefabUtility.SavePrefabAsset(identity.gameObject);
        AssetDatabase.SaveAssets();
        ValidateAndReport(spells);
    }

    // копии сохраняют первый исходный вариант и не перезаписываются при повторном запуске.
    private static void Backup(string path)
    {
        string target = BackupFolder + "/" + Path.GetFileName(path);
        if (!File.Exists(target)) File.Copy(path, target);
    }

    private static int Damage(Spell spell)
    {
        if (spell is ElementalSpell elemental) return elemental.damage;
        if (spell is TacticalSpell tactical) return tactical.damage;
        if (spell is FireBall fire) return new SerializedObject(fire.PreviewPrefab.GetComponent<FireballProjectile>()).FindProperty("fireballDamage").intValue;
        return 0;
    }

    private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Recipe(Spell spell) => string.Join(" → ", spell.Recipe.Select(ElementLoadout.Label));

    // описываем фактический эффект, оставляя кулдаун отдельному полю интерфейса.
    private static string Description(Spell spell, int damage)
    {
        if (spell is FireBall) return $"Одиночный огненный снаряд: {damage} базового урона. Без площадного урона.";
        if (spell is WindFlow) return "Порыв воздуха сильно отталкивает противника. Не наносит урон.";
        if (spell is ElementalSpell e)
        {
            if (e.mode == ElementalCastMode.Shield) return $"Поглощает до {e.shieldAmount} урона в течение {N(e.duration)} с. Разрушается при исчерпании защиты.";
            if (spell.name == "SmokeCloud") return $"Дымовая завеса на {N(e.duration)} с. Закрывает обзор, не наносит урон и не замедляет.";
            string text = e.mode == ElementalCastMode.Bolt ? $"Снаряд: {damage} базового урона." :
                e.mode == ElementalCastMode.SelfBurst ? $"Волна вокруг мага: {damage} базового урона один раз." :
                $"{(e.mode == ElementalCastMode.Tornado ? "Движущийся вихрь" : "Область")} на {N(e.duration)} с: {damage} базового урона каждые {N(e.tickInterval)} с.";
            if (e.radius > .5f) text += $" Радиус {N(e.radius)} м.";
            if (e.slow < 1) text += $" Замедляет на {N((1-e.slow)*100)}% на {N(e.slowDuration)} с после воздействия.";
            if (e.knockback > 0) text += " Отталкивает.";
            if (e.knockback < 0) text += " Притягивает.";
            if (e.lift > 0) text += " Подбрасывает.";
            return text;
        }
        var t = (TacticalSpell)spell;
        switch (t.kind)
        {
            case TacticalKind.SteamDash: return "Рывок по направлению движения примерно на 5 м. Не даёт неуязвимость. Стены останавливают рывок.";
            case TacticalKind.IceMirror: return $"Зеркало перед магом на {N(t.duration)} с. Отражает один вражеский снаряд и разрушается.";
            case TacticalKind.StoneWall: return $"Стена высотой 2,7 м на {N(t.duration)} с. Блокирует проход и снаряды. Без цели на полу ставится перед магом на свободной земле.";
            case TacticalKind.FireSeal: return $"Ловушка на {N(t.duration)} с; взводится за 1 с. Враг в радиусе 1,25 м вызывает взрыв: {damage} базового урона в радиусе {N(t.radius)} м.";
            case TacticalKind.GravityWell: return $"Узел на {N(t.duration)} с притягивает врагов в радиусе {N(t.radius)} м. Не действует через стены и не наносит урон.";
            default: return $"Двойник бежит вперёд {N(t.duration)} с. При попадании в него замедляет ближайшего врага в радиусе 3 м на 50% на 2 с. Не наносит урон.";
        }
    }

    // проверяем точные рецепты, все 125 последовательностей и доступность каждого набора из трёх стихий.
    private static void ValidateAndReport(Spell[] spells)
    {
        var keys = new HashSet<string>();
        foreach (Spell spell in spells)
        {
            var rule = Rules.Single(r => r.id == spell.name);
            if (Damage(spell) != rule.damage || !Mathf.Approximately(spell.Cooldown, rule.cooldown)) throw new InvalidOperationException("Balance mismatch: " + spell.name);
            if (spell.Recipe.Count != 3 || !keys.Add(string.Join(",", spell.Recipe))) throw new InvalidOperationException("Duplicate ordered recipe.");
        }
        for (int a = 0; a < 5; a++) for (int b = 0; b < 5; b++) for (int c = 0; c < 5; c++)
            if (spells.Count(s => s.MatchesCombo(new[] { (MagicElement)a, (MagicElement)b, (MagicElement)c })) > 1)
                throw new InvalidOperationException("Ambiguous combo.");
        var report = new StringBuilder("# Числовой баланс заклинаний — первый проход\n\n");
        report.AppendLine("Это исходная настройка для плейтестов, не доказанный соревновательный баланс. Урон указан базовый: фактически применяется разброс ±10%, для прямого попадания в голову — множитель 1,5. У игрока 100 здоровья. Порядок стихий важен.\n");
        report.AppendLine("## Все 21 заклинание\n\n| Заклинание | Рецепт | Урон до → после | Кулдаун до → после, с | Причина |\n|---|---|---:|---:|---|");
        foreach (var rule in Rules)
        {
            var spell = spells.Single(s => s.name == rule.id);
            report.AppendLine($"| {spell.Name} | {Recipe(spell)} | {rule.beforeDamage} → {rule.damage} | {N(rule.beforeCooldown)} → {N(rule.cooldown)} | {rule.reason} |");
        }
        report.AppendLine("\nУрон зон указан **за один тик**, а не за всё время. Ноль у утилитарного заклинания не означает отсутствие силы.\n");
        report.AppendLine("## Полный доступ к заклинаниям по наборам\n\n| Набор | Число | Заклинания | Урон залпа снарядами до → после | Сумма урон/кулдаун снарядов до → после |\n|---|---:|---|---:|---:|");
        for (int a = 0; a < 3; a++) for (int b = a + 1; b < 4; b++) for (int c = b + 1; c < 5; c++)
        {
            var loadout = new ElementLoadout { q = (MagicElement)a, e = (MagicElement)b, r = (MagicElement)c };
            var available = spells.Where(s => s.IsAvailable(loadout)).ToArray();
            var bolts = available.Where(s => s is FireBall || s is ElementalSpell e && e.mode == ElementalCastMode.Bolt).ToArray();
            var rules = bolts.Select(s => Rules.Single(r => r.id == s.name)).ToArray();
            report.AppendLine($"| {string.Join(" + ", new[] { loadout.q, loadout.e, loadout.r }.Select(ElementLoadout.Label))} | {available.Length} | {string.Join(", ", available.Select(s => s.Name))} | {rules.Sum(r => r.beforeDamage)} → {rules.Sum(r => r.damage)} | {N(rules.Sum(r => r.beforeDamage/r.beforeCooldown))} → {N(rules.Sum(r => r.damage/r.cooldown))} |");
        }
        report.AppendLine("\nЗалп — по одному попаданию каждым доступным снарядом, без зон и волны вокруг мага. Это не мгновенный урон и не реальный DPS: игнорируются ввод комбинаций, промахи, полёт, укрытия, защита и число целей. Сумма урон/кулдаун — только сравнение доступного дальнего урона при идеальных попаданиях.\n");
        report.AppendLine("Подробнее о сильных/слабых сторонах всех наборов и проблемах механик: [разбор сочетаний](SpellBalanceAnalysis.md)." );
        Directory.CreateDirectory("Docs");
        File.WriteAllText("Docs/SpellBalanceV1.md", report.ToString());
        File.WriteAllText(BackupFolder + "/validation.txt", "PASS: 21 spell values; 21 unique ordered recipes; 125 input sequences; 10 element loadouts; Fireball prefab damage updated.");
    }
}
