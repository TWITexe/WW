using System;
using System.Linq;
using Mirror;
using TMPro;
using UnityEngine;

public partial class PlayerGameUI
{
    [SerializeField] private TMP_Text matchTimer, matchCaption;
    [SerializeField] private GameObject resultsPanel;
    [SerializeField] private TMP_Text resultReason, resultNames, resultKills, resultDeaths, rematchStatus;
    [SerializeField] private UnityEngine.UI.Button rematchButton, resultsMenuButton;
    [SerializeField] private RectTransform resultsContent;
    int displayedRound = -1;
    bool votePending;
    [SerializeField] private TMP_Text rewardSummary;
    float nextWalletRefresh;
    void BindMatchUI()
    {
        if (resultsPanel == null) return;
        resultsPanel.SetActive(false);
        rematchButton.onClick.AddListener(() =>
        {
            votePending = true;
            rematchButton.interactable = false;
            rematchStatus.text = "Ожидание других игроков";
            NetManager.Room?.VoteRematch();
        });
        resultsMenuButton.onClick.AddListener(ReturnToMenu);
    }
    bool UpdateMatchUI()
    {
        var manager = NetManager.Room;
        if (manager == null || matchTimer == null) return false;
        var state = manager.State;
        double remaining = Math.Max(0, state.endsAt - NetworkTime.time);
        matchTimer.text = state.phase == MatchPhase.Playing ? MatchRules.Clock(remaining) : "00:00";
        matchTimer.color = MatchRules.TimerColor(remaining, Math.Max(1, state.rules.minutes * 60));
        matchCaption.text = $"Схватка · до {state.rules.killGoal} убийств";
        if (state.phase != MatchPhase.Results) return false;
        if (!resultsPanel.activeSelf)
        {
            pause.SetActive(false); scoreboard.SetActive(false);
            resultsPanel.SetActive(true);
            InputBlocked = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        if (displayedRound != state.round)
        {
            displayedRound = state.round; votePending = false;
            var rows = state.standings ?? Array.Empty<MatchStanding>();
            resultReason.text = state.reason;
            resultNames.text = string.Join("\n", rows.Select((p, i) => $"{i + 1}. {p.name}"));
            resultKills.text = string.Join("\n", rows.Select(p => p.kills));
            resultDeaths.text = string.Join("\n", rows.Select(p => p.deaths));
            float height = Mathf.Max(300, rows.Length * 38);
            resultsContent.sizeDelta = new Vector2(0, height);
            foreach (var label in new[] { resultNames, resultKills, resultDeaths })
                label.rectTransform.sizeDelta = new Vector2(label.rectTransform.sizeDelta.x, height);
        }
        bool voted = state.voted || votePending;
        if (rewardSummary != null)
        {
            var economy = manager.EconomyState;
            var account = EconomyClient.Instance;
            var profile = account?.Profile;
            bool received = profile != null && profile.lastMatch == economy.matchId && !string.IsNullOrEmpty(economy.matchId);
            rewardSummary.text = received ? $"Получено: +{profile.lastReward} W   ·   Баланс: {profile.coins} W" :
                economy.eligible ? (account != null && !account.Connected ? account.Status : "Награда сохраняется…") : "Без награды: нужны 2 игрока и участие до конца матча";
            if (economy.eligible && !received && Time.unscaledTime >= nextWalletRefresh)
            { nextWalletRefresh = Time.unscaledTime + 5; account?.Refresh(); }
        }
        rematchButton.interactable = !voted && NetworkTime.time < state.rematchAt && !manager.Leaving;
        rematchStatus.text = (voted ? "Ожидание других игроков" : "Нажмите «Реванш», чтобы сыграть снова") +
            $"\nГотовы: {state.votes} / {state.players} · Реванш через {MatchRules.Clock(state.rematchAt - NetworkTime.time)}";
        return true;
    }
}
