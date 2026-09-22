using System;
using Mirror;
using UnityEditor;
using UnityEngine;

// проверяет назначения стихий, распознавание рецептов и сетевые компоненты исходных снарядов.
public static class SpellSystemValidation
{
    // проверяем корректные и ошибочные наборы, перестановки нажатий и ссылки в префабах.
    [MenuItem("Tools/Wizard War/Validate spell system")]
    public static void Run()
    {
        int checks = 0;
        Action<bool, string> check = (condition, message) =>
        {
            if (!condition) throw new Exception("Spell validation: " + message);
            checks++;
        };
        var loadout = ElementLoadout.Default;
        check(loadout.IsValid, "default loadout");
        for (int slot = 0; slot < 3; slot++)
            for (int element = 0; element < 4; element++)
            {
                var swapped = loadout;
                swapped.Assign(slot, (MagicElement)element);
                check(swapped.IsValid && swapped.Get(slot) == (MagicElement)element, "swap preserves uniqueness");
            }
        check(!new ElementLoadout().IsValid, "duplicate elements rejected");
        var invalid = loadout;
        invalid.q = (MagicElement)99;
        check(!invalid.IsValid, "unknown element rejected");

        var fire = AssetDatabase.LoadAssetAtPath<FireBall>("Assets/Scripts/Spells/FireBall/FireBall.asset");
        var wind = AssetDatabase.LoadAssetAtPath<WindFlow>("Assets/Scripts/Spells/WindFlow/WindFlow.asset");
        check(fire != null && wind != null, "spell assets load");
        check(fire.IsAvailable(loadout) && !wind.IsAvailable(loadout), "wind now requires earth");
        check(loadout.KeysFor(fire.Recipe) == "Q → Q → Q", "default fire keys");
                var dash = AssetDatabase.LoadAssetAtPath<TacticalSpell>("Assets/Scripts/Spells/Tactical/SteamDash.asset");
        check(dash.MatchesCombo(new[] { MagicElement.Air, MagicElement.Air, MagicElement.Air }), "triple air resolves dash");
        var earthAir = new ElementLoadout { q = MagicElement.Earth, e = MagicElement.Air, r = MagicElement.Ice };
        check(wind.MatchesCombo(new[] { MagicElement.Earth, MagicElement.Air, MagicElement.Air }), "ordered earth-air-air resolves wind");
        check(!wind.MatchesCombo(new[] { MagicElement.Air, MagicElement.Earth, MagicElement.Air }), "wind rejects reordered elements");
        check(earthAir.KeysFor(wind.Recipe) == "Q → E → E", "new wind keys");
        loadout.Assign(2, MagicElement.Fire);
        check(loadout.KeysFor(fire.Recipe) == "R → R → R", "rebinding changes displayed recipe");
        check(!fire.IsAvailable(invalid), "invalid loadout rejected by catalog");
        var noFire = ElementLoadout.Default;
        noFire.Assign(0, MagicElement.Earth);
        check(!fire.IsAvailable(noFire) && wind.IsAvailable(noFire), "catalog filters unavailable elements");
        check(!fire.MatchesCombo(new[] { MagicElement.Fire, MagicElement.Fire }), "partial recipe rejected");
        check(!fire.MatchesCombo(null), "null input rejected");
        check(!fire.MatchesCombo(new[] { MagicElement.Fire, MagicElement.Fire, MagicElement.Fire, MagicElement.Fire }), "long input rejected");

        // проверяем все двадцать семь последовательностей: подходит только точный порядок рецепта.
        var mixed = ScriptableObject.CreateInstance<FireBall>();
        try
        {
            var serialized = new SerializedObject(mixed);
            var recipe = serialized.FindProperty("recipe");
            recipe.arraySize = 3;
            recipe.GetArrayElementAtIndex(0).enumValueIndex = 0;
            recipe.GetArrayElementAtIndex(1).enumValueIndex = 1;
            recipe.GetArrayElementAtIndex(2).enumValueIndex = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++)
                    for (int c = 0; c < 3; c++)
                    {
                        check(mixed.MatchesCombo(new[] { (MagicElement)a, (MagicElement)b, (MagicElement)c }) ==
                            (a == 0 && b == 1 && c == 0), "ordered matching " + a + b + c);
                    }
        }
        finally { UnityEngine.Object.DestroyImmediate(mixed); }

        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        var manager = player.GetComponentInChildren<SpellManager>(true);
        check(manager != null && manager.Spells.Count >= 2, "player spell catalog preserved");
        check(manager.FindSpell(fire.Recipe, loadout) >= 0, "player resolves fire recipe");
        foreach (string path in new[] { "Assets/Prefabs/FireBall.prefab", "Assets/Prefabs/WindFlow.prefab" })
        {
            var projectile = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            check(projectile.GetComponent<NetworkIdentity>() != null, "projectile network identity");
            check(projectile.GetComponent<Rigidbody>() != null, "projectile rigidbody");
            check(projectile.GetComponent<NetworkTransformReliable>().syncDirection == SyncDirection.ServerToClient,
                "server controls projectile transform");
        }
        System.IO.File.WriteAllText("Logs/spell-system-validation.txt", $"PASS: {checks} checks");
        Debug.Log($"SPELL_VALIDATION_PASSED: {checks} checks");
    }
}
