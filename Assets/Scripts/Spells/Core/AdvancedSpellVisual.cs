using Mirror;
using UnityEngine;

// визуальные фазы привязаны к серверным часам; частицы не участвуют в расчёте попаданий.
public class AdvancedSpellVisual : MonoBehaviour
{
    public AdvancedSpellEffect effect;
    public GameObject warning, projectile, area, fallingBody;
    public GameObject impactPrefab, extraImpactPrefab;
    public Transform[] healthMarks;
    private int shownPhase = -1;

    private void Start()
    {
        if (NetworkServer.active && !NetworkClient.active)
        {
            Set(warning, false); Set(projectile, false); Set(area, false); Set(fallingBody, false);
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (effect == null || effect.definition == null) return;
        var spell = effect.definition;
        double elapsed = NetworkTime.time - effect.phaseStarted;
        if (shownPhase != effect.phase)
        {
            shownPhase = effect.phase;
            Set(warning, (effect.phase == 0 && spell.HasWarning) || effect.phase == 3);
            Set(projectile, (effect.phase == 0 || effect.phase == 3) && spell.IsProjectile);
            Set(area, effect.phase == 1);
            if (spell.kind == AdvancedSpellKind.SteamLens && effect.phase == 2 && elapsed < .8) Burst(impactPrefab);
            // позднее подключение показывает действующую область, но не повторяет старый взрыв.
            if (effect.phase == 1 && elapsed < .8 && (spell.HasWarning || spell.IsProjectile))
            {
                Burst(impactPrefab);
                Burst(extraImpactPrefab);
                SpellSurfaceMark.Place(transform.position + Vector3.up * .15f,
                    spell.kind == AdvancedSpellKind.Meteor || spell.kind == AdvancedSpellKind.ScaldingMist
                        ? SpellHitKind.Fire : SpellHitKind.Snow, spell.radius);
            }
        }
        if (fallingBody != null)
        {
            float fallDuration = spell.kind == AdvancedSpellKind.Meteor ? .8f : .35f;
            bool falling = effect.phase == 0 && elapsed >= spell.delay - fallDuration;
            Set(fallingBody, falling);
            if (falling) fallingBody.transform.localPosition = Vector3.up * Mathf.Lerp(12, 0, Mathf.Clamp01((float)(elapsed - spell.delay + fallDuration) / fallDuration));
        }
        if (healthMarks != null && effect.phase == 1)
            for (int i = 0; i < healthMarks.Length; i++)
            {
                float angle = i * Mathf.PI * 2 / healthMarks.Length;
                healthMarks[i].localPosition = new Vector3(Mathf.Cos(angle), Mathf.Repeat((float)elapsed * .65f + i * .23f, 1.6f) + .15f, Mathf.Sin(angle));
                healthMarks[i].Rotate(0, 30 * Time.deltaTime, 0, Space.World);
            }
    }

    private static void Set(GameObject root, bool active)
    {
        if (root != null && root.activeSelf != active) root.SetActive(active);
    }

    private void Burst(GameObject prefab)
    {
        if (prefab == null) return;
        var visual = Instantiate(prefab, transform.position, Quaternion.identity);
        Destroy(visual, 7);
    }
}
