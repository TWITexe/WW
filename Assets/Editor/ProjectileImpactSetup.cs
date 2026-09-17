using System;
using UnityEditor;
using UnityEngine;

public static class ProjectileImpactSetup
{
    public static void Apply()
    {
        var go=new GameObject("SpellProjectileImpact",typeof(ParticleSystem));
        try
        {
            var particles=go.GetComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=particles.main;main.loop=false;main.playOnAwake=false;main.duration=1;main.startLifetime=new ParticleSystem.MinMaxCurve(.4f,1f);
            main.startSize=new ParticleSystem.MinMaxCurve(.1f,.25f);main.startSpeed=new ParticleSystem.MinMaxCurve(2,5);main.maxParticles=160;
            main.simulationSpace=ParticleSystemSimulationSpace.World;main.gravityModifier=.15f;
            var emission=particles.emission;emission.rateOverTime=0;
            var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.08f;
            var fade=particles.colorOverLifetime;fade.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.8f,.3f),new GradientAlphaKey(0,1)});fade.color=gradient;
            var shrink=particles.sizeOverLifetime;shrink.enabled=true;shrink.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,1,1,0));
            var renderer=particles.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/GeneratedWizard/ReadableSpellParticles.mat");renderer.renderMode=ParticleSystemRenderMode.Billboard;
            if(renderer.sharedMaterial==null||go.GetComponent<Collider>()!=null)throw new Exception("Invalid impact visual");
            PrefabUtility.SaveAsPrefabAsset(go,"Assets/Resources/SpellProjectileImpact.prefab");
            AssetDatabase.SaveAssets();Debug.Log("PROJECTILE_IMPACT_SETUP_PASSED");
        }
        finally{UnityEngine.Object.DestroyImmediate(go);}
    }
}

