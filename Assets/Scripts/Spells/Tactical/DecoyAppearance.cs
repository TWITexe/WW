using UnityEngine;

// локальные декоративные обломки не участвуют в попаданиях заклинаний и движении игроков.
// копирует оформление мага на двойника и создаёт декоративные обломки при его разрушении.
public static class DecoyAppearance
{
    // переносим общие материалы и индивидуальную окраску, включая отдельные слоты материалов.
    public static void Copy(Renderer source, Renderer target)
    {
        target.sharedMaterials=source.sharedMaterials;
        var block=new MaterialPropertyBlock();
        source.GetPropertyBlock(block);
        target.SetPropertyBlock(block.isEmpty?null:block);
        for(int i=0;i<source.sharedMaterials.Length;i++)
        {
            block.Clear();source.GetPropertyBlock(block,i);
            // пустой блок отдельного материала не должен перекрывать общую окраску мантии или шляпы.
            target.SetPropertyBlock(block.isEmpty?null:block,i);
        }
    }
    // разбиваем двойника на локальные части; обломки не мешают игрокам и исчезают через три секунды.
    public static void Collapse(Transform model)
    {
        if(model==null)return;
        var players=Object.FindObjectsByType<Health>(FindObjectsSortMode.None);
        foreach(Transform piece in model)
        {
            if(piece.name!="Head"&&piece.name!="Hat"&&piece.name!="Body"&&piece.name!="Staff")continue;
            var fragment=Object.Instantiate(piece.gameObject,piece.position,piece.rotation);
            fragment.name="Decoy debris - "+piece.name;fragment.transform.localScale=piece.lossyScale;
            var originals=piece.GetComponentsInChildren<Renderer>(true);
            var copies=fragment.GetComponentsInChildren<Renderer>(true);
            for(int i=0;i<Mathf.Min(originals.Length,copies.Length);i++)Copy(originals[i],copies[i]);
            Bounds bounds=default;bool first=true;
            foreach(var renderer in copies)
                for(int i=0;i<8;i++)
                {
                    var world=renderer.bounds;
                    var corner=world.center+Vector3.Scale(world.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    var local=fragment.transform.InverseTransformPoint(corner);
                    if(first){bounds=new Bounds(local,Vector3.zero);first=false;}else bounds.Encapsulate(local);
                }
            foreach(var child in fragment.GetComponentsInChildren<Transform>(true))child.gameObject.layer=2;
            var collider=fragment.AddComponent<BoxCollider>();collider.center=bounds.center;collider.size=bounds.size;
            foreach(var player in players)foreach(var other in player.GetComponentsInChildren<Collider>())Physics.IgnoreCollision(collider,other);
            var body=fragment.AddComponent<Rigidbody>();body.mass=piece.name=="Body"?2:.6f;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            body.AddExplosionForce(5,model.position+Vector3.down*.3f,4,1,ForceMode.Impulse);
            body.AddTorque(Random.onUnitSphere*3,ForceMode.Impulse);
            Object.Destroy(fragment,3f);
        }
    }
}
