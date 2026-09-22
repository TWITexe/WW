using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// Панель под каталогом: имя нового набора и мгновенный выбор сохранённого.
public class SavedElementBuildUI : MonoBehaviour
{
    [SerializeField] private TMP_InputField buildName;
    [SerializeField] private TMP_Dropdown buildPicker;
    [SerializeField] private UnityEngine.UI.Button saveButton;
    [SerializeField] private UnityEngine.UI.Button deleteButton;
    [SerializeField] private TMP_Text saveLabel;
    [SerializeField] private TMP_Text deleteLabel;
    [SerializeField] private TMP_Text status;
    [SerializeField] private CanvasGroup inputGroup;
    private InteractiveShelfMenu menu;
    private readonly List<string> optionNames = new List<string>();
    private string selectedName;
    private string pendingReplace, pendingDelete;
    private ElementLoadout pendingLoadout;
    private int lastTextInputFrame = -2;
    private bool bound;
    private SavedElementBuilds Store => LocalPlayerSettings.Instance?.SavedBuilds;
    public bool HandlesInput => buildName.isFocused || buildPicker.IsExpanded || lastTextInputFrame >= Time.frameCount - 1;

    public void Bind(InteractiveShelfMenu owner)
    {
        menu = owner;
        if (!bound)
        {
            saveButton.onClick.AddListener(Save);
            deleteButton.onClick.AddListener(Delete);
            buildPicker.onValueChanged.AddListener(Select);
            buildName.onValueChanged.AddListener(_ => ResetConfirmation());
            bound = true;
        }
        RebuildOptions();
        ResetConfirmation();
        Refresh(LocalPlayerSettings.Instance != null ? LocalPlayerSettings.Instance.Loadout : ElementLoadout.Default);
    }

    private void LateUpdate()
    {
        if (!buildName.isFocused && !buildPicker.IsExpanded) return;
        lastTextInputFrame = Time.frameCount;
        if (Input.GetMouseButtonDown(1)) ClearFocus();
    }

    public void SetInteraction(bool enabled)
    {
        inputGroup.interactable = enabled;
        inputGroup.blocksRaycasts = enabled;
        if (!enabled)
        {
            ClearFocus();
            ResetConfirmation();
        }
    }

    private void ClearFocus()
    {
        buildPicker.Hide();
        buildName.DeactivateInputField();
        var events = EventSystem.current;
        if (events != null && events.currentSelectedGameObject != null &&
            events.currentSelectedGameObject.transform.IsChildOf(transform)) events.SetSelectedGameObject(null);
    }

    public void Refresh(ElementLoadout loadout)
    {
        if (Store == null) return;
        if (pendingReplace != null && !Same(pendingLoadout, loadout)) ResetConfirmation();
        string previousName = selectedName;
        int index = Store.Find(selectedName);
        if (index < 0 || !Same(Store.Builds[index].Loadout, loadout))
        {
            selectedName = null;
            foreach (var build in Store.Builds)
                if (Same(build.Loadout, loadout)) { selectedName = build.Name; break; }
        }
        if (pendingDelete != null && selectedName != previousName) ResetConfirmation();
        buildPicker.SetValueWithoutNotify(selectedName == null ? 0 : optionNames.IndexOf(selectedName) + 1);
        buildPicker.RefreshShownValue();
        deleteButton.interactable = selectedName != null;
    }

    private static bool Same(ElementLoadout a, ElementLoadout b) => a.q == b.q && a.e == b.e && a.r == b.r;

    private void RebuildOptions()
    {
        optionNames.Clear();
        var options = new List<string> { Store == null || Store.Builds.Count == 0 ? "Нет сохранённых билдов" : "Текущий набор" };
        if (Store != null)
            foreach (var build in Store.Builds) { optionNames.Add(build.Name); options.Add(build.Name); }
        buildPicker.ClearOptions();
        buildPicker.AddOptions(options);
        buildPicker.interactable = optionNames.Count > 0;
    }

    private void Save()
    {
        if (menu == null || !menu.IsFocused || Store == null) return;
        string name = buildName.text.Trim();
        if (!SavedElementBuilds.ValidName(name))
        {
            status.text = "Введите название билда (до 32 символов).";
            buildName.ActivateInputField();
            return;
        }
        var loadout = LocalPlayerSettings.Instance.Loadout;
        if (Store.Find(name) >= 0 && pendingReplace != name)
        {
            ResetConfirmation();
            pendingReplace = name;
            pendingLoadout = loadout;
            saveLabel.text = "Заменить?";
            status.text = "Имя уже занято. Нажмите ещё раз для замены.";
            return;
        }
        if (!Store.Save(name, loadout, pendingReplace == name))
        {
            status.text = "Нет места: удалите ненужный билд (лимит 60).";
            return;
        }
        selectedName = name;
        buildName.SetTextWithoutNotify("");
        ResetConfirmation();
        RebuildOptions();
        Refresh(loadout);
        status.text = "Сохранено: " + name;
    }

    private void Select(int option)
    {
        if (menu == null || Store == null) return;
        ResetConfirmation();
        if (option > 0 && option <= optionNames.Count)
        {
            string name = optionNames[option - 1];
            int index = Store.Find(name);
            if (index >= 0)
            {
                selectedName = name;
                if (menu.ApplyBuild(Store.Builds[index].Loadout)) status.text = "Выбран: " + name;
            }
        }
        Refresh(LocalPlayerSettings.Instance.Loadout);
    }

    private void Delete()
    {
        if (menu == null || !menu.IsFocused || Store == null || selectedName == null) return;
        if (pendingDelete != selectedName)
        {
            ResetConfirmation();
            pendingDelete = selectedName;
            deleteLabel.text = "Точно?";
            status.text = "Нажмите ещё раз, чтобы удалить выбранный билд.";
            return;
        }
        Store.Delete(selectedName);
        selectedName = null;
        ResetConfirmation();
        RebuildOptions();
        Refresh(LocalPlayerSettings.Instance.Loadout);
        status.text = "Билд удалён. Выбранные стихии сохранены.";
    }

    private void ResetConfirmation()
    {
        pendingReplace = pendingDelete = null;
        saveLabel.text = "Сохранить билд";
        deleteLabel.text = "Удалить";
        status.text = Store == null || Store.Builds.Count == 0
            ? "Назовите текущий набор и нажмите «Сохранить билд»."
            : "Выберите сохранённый билд, чтобы сменить стихии.";
    }

#if UNITY_EDITOR
    public void Configure(TMP_InputField nameInput, TMP_Dropdown picker, UnityEngine.UI.Button save,
        UnityEngine.UI.Button delete, TMP_Text saveText, TMP_Text deleteText, TMP_Text message, CanvasGroup group)
    {
        buildName = nameInput; buildPicker = picker; saveButton = save; deleteButton = delete;
        saveLabel = saveText; deleteLabel = deleteText; status = message; inputGroup = group;
        ResetConfirmation();
    }
#endif
}
