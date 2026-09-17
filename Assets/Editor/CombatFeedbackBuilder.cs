using System;
using TMPro;
using UnityEditor;
using UnityEngine;
public static class CombatFeedbackBuilder
{
 public static void Apply()
 {
  string wallPath="Assets/TacticalSpells/StoneWall.prefab";var wall=PrefabUtility.LoadPrefabContents(wallPath);
  try {var box=wall.GetComponent<BoxCollider>();float ratio=2.7f/box.size.y;foreach(Transform child in wall.transform){var p=child.localPosition;p.y*=ratio;child.localPosition=p;var scale=child.localScale;scale.y*=ratio;child.localScale=scale;}box.size=new Vector3(3.8f,2.7f,.55f);box.center=Vector3.up*1.35f;PrefabUtility.SaveAsPrefabAsset(wall,wallPath);}finally{PrefabUtility.UnloadPrefabContents(wall);}
  var spell=AssetDatabase.LoadAssetAtPath<TacticalSpell>("Assets/TacticalSpells/StoneWall.asset");var data=new SerializedObject(spell);data.FindProperty("description").stringValue="Стена высотой 2,7 м на 5 с. Перекрывает проход и снаряды. Только на свободной земле.";data.ApplyModifiedPropertiesWithoutUndo();
  var go=new GameObject("DamageNumber",typeof(TextMeshPro),typeof(FloatingDamageNumber));
  var label=go.GetComponent<TextMeshPro>();label.text="25";label.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");label.fontSize=5;label.alignment=TextAlignmentOptions.Center;label.rectTransform.sizeDelta=new Vector2(2,1);label.raycastTarget=false;go.GetComponent<FloatingDamageNumber>().label=label;
  PrefabUtility.SaveAsPrefabAsset(go,"Assets/Resources/DamageNumber.prefab");UnityEngine.Object.DestroyImmediate(go);
  var impact=PrefabUtility.LoadPrefabContents("Assets/Resources/SpellProjectileImpact.prefab");try{var ps=impact.GetComponent<ParticleSystem>();var main=ps.main;main.maxParticles=160;main.startLifetime=new ParticleSystem.MinMaxCurve(.4f,1f);main.startSize=new ParticleSystem.MinMaxCurve(.1f,.25f);PrefabUtility.SaveAsPrefabAsset(impact,"Assets/Resources/SpellProjectileImpact.prefab");}finally{PrefabUtility.UnloadPrefabContents(impact);}
  AssetDatabase.SaveAssets();
  if(SpellDamage.Roll(20,false,0)!=18||SpellDamage.Roll(20,false,1)!=22||SpellDamage.Roll(20,true,.5f)!=30||SpellDamage.Roll(0,true,1)!=0)throw new Exception("Damage bounds");
  Debug.Log("COMBAT_FEEDBACK_BUILT_AND_DAMAGE_BOUNDS_PASSED");
 }
}
