using System.Collections.Generic;
using UnityEngine;

// короткая история убийств хранится только на клиенте текущего матча.
public static class MatchKillFeed
{
    private struct Entry { public string text; public float expires; }
    private static readonly Queue<Entry> entries = new Queue<Entry>();
    private static string cachedText = "";
    private static int cachedRevision = -1;
    public static int Revision { get; private set; }
    public static void Clear() { entries.Clear(); Revision++; }
    public static string ColoredName(string name, Color color)
        => "<color=#"+ColorUtility.ToHtmlStringRGB(Color.Lerp(color,Color.white,.3f))+">"+name.Replace("<", "").Replace(">", "")+"</color>";
    public static void Add(string killer,string victim,Color killerColor,Color victimColor)
    {
        while(entries.Count>=5)entries.Dequeue();
        entries.Enqueue(new Entry { text=ColoredName(killer,killerColor)+"  →  "+ColoredName(victim,victimColor), expires=Time.unscaledTime+7 });
        Revision++;
    }
    public static string Read()
    {
        while(entries.Count>0 && entries.Peek().expires<=Time.unscaledTime) {entries.Dequeue();Revision++;}
        if(cachedRevision==Revision)return cachedText;
        var text=new System.Text.StringBuilder();
        foreach(var entry in entries)text.AppendLine(entry.text);
        cachedRevision=Revision;
        cachedText=text.ToString();
        return cachedText;
    }
}
