using System.Collections.Generic;
using UnityEngine;

// хранит до трёх последних стихий для локального отображения комбинации.
public class InputComboTracker : MonoBehaviour
{
    public const float InputTimeout = 2f;
    private readonly List<MagicElement> history = new List<MagicElement>(3);
    private readonly List<ulong> revisions = new List<ulong>(3);
    public ulong Revision { get; private set; }
    private float lastInput;
    public IReadOnlyList<MagicElement> History => history;
    // сбрасываем просроченную комбинацию и сдвигаем историю, если три позиции уже заняты.
    public void AddElement(MagicElement element)
    {
        Expire();
        if (history.Count == 3)
        {
            history.RemoveAt(0);
            revisions.RemoveAt(0);
        }
        history.Add(element);
        revisions.Add(++Revision);
        lastInput = Time.time;
    }
    // очищаем ввод после двух секунд без нового нажатия.
    public void Expire()
    {
        if (Time.time - lastInput >= InputTimeout) Clear();
    }
    // сбрасываем локальную историю; серверная история ведётся отдельно.
    public void Clear()
    {
        history.Clear();
        revisions.Clear();
    }
    // подтверждение расходует только старые нажатия, сохраняя ввод после успешного заклинания.
    public void ClearThrough(ulong revision)
    {
        while (revisions.Count > 0 && revisions[0] <= revision)
        {
            history.RemoveAt(0);
            revisions.RemoveAt(0);
        }
    }
}
