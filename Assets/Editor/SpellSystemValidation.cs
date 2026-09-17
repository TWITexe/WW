using System;
using Mirror;
using UnityEditor;
using UnityEngine;

public static class SpellSystemValidation
{
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
        check(fire.IsAvailable(loadout) && wind.IsAvailable(loadout), "default spell availability");
        check(loadout.KeysFor(fire.Recipe) == "Q → Q → Q", "default fire keys");
        check(loadout.KeysFor(wind.Recipe) == "E → E → E", "default wind keys");
        loadout.Assign(2, MagicElement.Fire);
        check(loadout.KeysFor(fire.Recipe) == "R → R → R", "rebinding changes displayed recipe");
        check(!fire.IsAvailable(invalid), "invalid loadout rejected by catalog");
        var noFire = ElementLoadout.Default;
        noFire.Assign(0, MagicElement.Earth);
        check(!fire.IsAvailable(noFire) && wind.IsAvailable(noFire), "catalog filters unavailable elements");
        check(!fire.MatchesCombo(new[] { MagicElement.Fire, MagicElement.Fire }), "partial recipe rejected");

        // Exercise all 27 triples against a mixed recipe, including multiplicity.
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
                        int fires = (a == 0 ? 1 : 0) + (b == 0 ? 1 : 0) + (c == 0 ? 1 : 0);
                        int airs = (a == 1 ? 1 : 0) + (b == 1 ? 1 : 0) + (c == 1 ? 1 : 0);
                        check(mixed.MatchesCombo(new[] { (MagicElement)a, (MagicElement)b, (MagicElement)c }) ==
                            (fires == 2 && airs == 1), "order-independent matching " + a + b + c);
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
        Debug.Log($"SPELL_VALIDATION_PASSED: {checks} checks");
    }
}
