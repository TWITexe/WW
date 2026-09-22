using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// воспроизводит исчезновение таймера после двух секунд ожидания ответа и проверяет прежние анимации посоха.
public static class CooldownStaffRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    public static void Run()
    {
        if (Application.isPlaying || NetworkServer.active) throw new InvalidOperationException("проверка требует остановленного матча");
        checks = 0;
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            CheckCooldowns();
            WizardPresentationRegression.Run();
            WizardAnimationRegression.Run();
            File.WriteAllText("Logs/cooldown-staff-validation.txt", $"PASS: {checks} проверок таймеров каталога; проверки прежних анимаций посоха и плавного возврата также пройдены.\n{DateTime.Now:O}");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (original.IsValid()) SceneManager.SetActiveScene(original);
        }
    }

    // один сценарий для каждого спелла: готовность на границе срока, задержка ответа, подтверждение и отказ.
    private static void CheckCooldowns()
    {
        var root = new GameObject("проверка таймеров", typeof(NetworkIdentity), typeof(PlayerNetworkCaster));
        var identity = root.GetComponent<NetworkIdentity>();
        typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", Private).Invoke(identity, null);
        typeof(NetworkIdentity).GetProperty("isLocalPlayer").SetValue(identity, true);
        var caster = root.GetComponent<PlayerNetworkCaster>();
        var server = (Dictionary<Spell, double>)Get(caster, "serverCooldowns");
        var client = (Dictionary<Spell, double>)Get(caster, "clientCooldowns");
        var versions = (Dictionary<Spell, uint>)Get(caster, "cooldownConfirmations");
        var previews = (IDictionary)Get(caster, "previews");
        var previewType = typeof(PlayerNetworkCaster).GetNestedType("CastPreview", BindingFlags.NonPublic);
        var catalog = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab").GetComponentInChildren<SpellManager>().Spells;
        uint token = 0;
        foreach (var spell in catalog)
        {
            double now = NetworkTime.time;
            double readyAt = now + spell.Cooldown;
            server[spell] = readyAt;
            Require((double)Call(caster, "ServerRemainingCooldown", spell, readyAt - .01) > 0, spell.Name + ": ранняя готовность");
            Require((double)Call(caster, "ServerRemainingCooldown", spell, readyAt) == 0, spell.Name + ": задержка после срока");
            typeof(NetworkIdentity).GetProperty("isServer").SetValue(identity, true);
            client[spell] = now;
            Require(caster.RemainingCooldown(spell) > 0, spell.Name + ": хост не использует серверный срок");
            typeof(NetworkIdentity).GetProperty("isServer").SetValue(identity, false);

            var preview = Activator.CreateInstance(previewType, true);
            previewType.GetField("spell").SetValue(preview, spell);
            previewType.GetField("predictedCooldown").SetValue(preview, readyAt);
            previewType.GetField("expiresAt").SetValue(preview, Time.unscaledTime - 1);
            client[spell] = readyAt;
            previews.Add(++token, preview);
            Call(caster, "UpdatePreviews");
            Require(caster.RemainingCooldown(spell) > 0 && previews.Contains(token), spell.Name + ": таймер исчез после окончания предварительной графики");

            // совпадение чисел не означает, что подтверждённый сервером срок всё ещё является предположением.
            Call(caster, "ConfirmCooldown", spell, readyAt);
            Call(caster, "FinishPreview", token, false);
            Require(client[spell] == readyAt, spell.Name + ": поздний отказ стёр подтверждённый срок");

            preview = Activator.CreateInstance(previewType, true);
            previewType.GetField("spell").SetValue(preview, spell);
            previewType.GetField("predictedCooldown").SetValue(preview, readyAt);
            previewType.GetField("confirmation").SetValue(preview, versions[spell]);
            previews.Add(++token, preview);
            Call(caster, "FinishPreview", token, false);
            Require(client[spell] == 0, spell.Name + ": явный отказ не отменил предварительную перезарядку");
        }
        Object.DestroyImmediate(root);
    }

    private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new Exception(reason);
        checks++;
    }
}
