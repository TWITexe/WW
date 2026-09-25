using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class DeathmatchRegression
{
    public static void RulesAndAssets()
    {
        Check(new MatchRules{minutes=0,killGoal=0}.Validated().minutes==1,"minimum time");
        Check(new MatchRules{minutes=100,killGoal=100}.Validated().killGoal==33,"maximum kills");
        Check(new MatchRules{minutes=100,killGoal=100}.Validated().minutes==30,"maximum time");
        Check(MatchRules.Clock(71)=="01:11"&&MatchRules.Clock(-1)=="00:00"&&MatchRules.Clock(.1)=="00:01","timer rounding");
        Check(MatchRules.TimerColor(180,600)==Color.white,"30 percent threshold");
        Check(MatchRules.TimerColor(0,600)==Color.red,"red at deadline");
        Check(MatchRules.TimerColor(90,600).g>.49f&&MatchRules.TimerColor(90,600).g<.51f,"smooth timer color");
        string key=RoomAuthenticator.PasswordKey("test");
        Check(RoomAuthenticator.Sign(key,"nonce","player")!=RoomAuthenticator.Sign(RoomAuthenticator.PasswordKey("wrong"),"nonce","player"),"wrong password proof");
        Check(RoomAuthenticator.Sign(key,"nonce","player")!=RoomAuthenticator.Sign(key,"other","player"),"replay challenge");
        Check(RoomAuthenticator.Sign(key,"nonce","player")!=RoomAuthenticator.Sign(key,"nonce","impostor"),"proof binds identity");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/MatchUI.prefab");
        var so=new SerializedObject(prefab.GetComponent<PlayerGameUI>());
        foreach(string field in new[]{"matchTimer","resultsPanel","resultsContent","rematchButton","resultsMenuButton"})
            Check(so.FindProperty(field).objectReferenceValue!=null,"HUD reference "+field);
        File.WriteAllText("Logs/deathmatch-rules-validation.txt","PASS: limits, clock, color thresholds, password proof and saved HUD references\n"+DateTime.UtcNow.ToString("O"));
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void BuildNetworkTest()
    {
        Directory.CreateDirectory("Logs/RoomNetworkTest/Build");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
            scenes=new[]{"Assets/Scenes/Menu.unity","Assets/Scenes/SampleScene.unity"},
            locationPathName="Logs/RoomNetworkTest/Build/RoomTest.exe",target=BuildTarget.StandaloneWindows64,
            options=BuildOptions.Development|BuildOptions.CompressWithLz4});
        File.WriteAllText("Logs/RoomNetworkTest/build-result.txt",report.summary.result+" errors="+report.summary.totalErrors);
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Network test build failed");
    }
}
