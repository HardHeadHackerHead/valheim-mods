using BuildOrders;
using UnityEngine;

int checks = 0;
void Check(bool ok,string message) { checks++; if (!ok) throw new Exception(message); }
var p = new Player(); Player.m_localPlayer = p;
ZNetScene.instance = new ZNetScene(); ObjectDB.instance = new ObjectDB();
ZNetScene.instance.Prefabs["wood_beam"] = new GameObject { Piece = new Piece { m_name = "beam" } };
ZNetScene.instance.Prefabs["piece_bo_bridge"] = new GameObject { Piece = new Piece { m_name = "bridge" } };
ZNetScene.instance.Prefabs["terrain"] = new GameObject { Piece = new Piece { m_name = "terrain" }, Terrain = new TerrainOp() };
var api = new Plugin(); Plugin.Instance = api;
string[] names = { "wood_beam", "wood_beam" };
Vector3[] poses = { new Vector3(1,0,0),new Vector3(3,0,0) };
Quaternion[] turns = { new Quaternion(0,0,0,1),new Quaternion(0,0,0,2) };
bool Create(out string key,out string error) => api.TryCreateGhostPlan(p,"Curve",names,poses,turns,out key,out error);
Check(Create(out string first,out _) && api.Orders.Count == 2, "Valid batch accepted");
Check(api.Saves == 1 && api.Sent.Count == 2 && api.Records.Contains(first) && api.StabilityDirty, "One save, ordinary sharing, named plan record, stability refresh");
Check(api.Orders.Values.All(o => o.Rot.w == 1), "Rotations normalized before serialization");
Check(!Create(out _,out _) && api.Orders.Count == 2 && api.Saves == 1, "Duplicate batch creates no empty plan");
// Simulate a piece already built through the normal planner. Undo removes only the remaining ghost.
api.Orders.Remove(api.Orders.Values.First().Id); api.Built.Add("beam building");
Check(api.TryRemoveGhostPlan(p,first,out int removed,out _) && removed == 1, "Undo removes remaining ghost only");
Check(api.Built.Contains("beam building") && api.Records.Contains(first) && api.Terrain.Contains("old terrain snapshot"), "Built pieces, built history, and stale terrain untouched");
Check(api.TryRemoveGhostPlan(p,first,out removed,out _) && removed == 0, "Undo is idempotent");
Check(Create(out string second,out _) && second != first, "Same title gets fresh immutable key");
Check(!api.TryRemoveGhostPlan(p,"Blueprint: old fort",out _,out _), "API cannot delete ordinary blueprint groups");
int before = api.Orders.Count, saved = api.Saves, sent = api.Sent.Count;
void Rejected(Func<bool> attempt,string name)
{ Check(!attempt(),name); Check(api.Orders.Count == before && api.Saves == saved && api.Sent.Count == sent,"Rejected batch is side-effect free"); }
Rejected(() => api.TryCreateGhostPlan(p,"bad|name",names,poses,turns,out _,out _),"Wire delimiter rejected");
Rejected(() => api.TryCreateGhostPlan(p,new string('x',81),names,poses,turns,out _,out _),"Title bound");
Rejected(() => api.TryCreateGhostPlan(p,"Curve",new string[257],new Vector3[257],new Quaternion[257],out _,out _),"Piece budget");
Rejected(() => api.TryCreateGhostPlan(p,"Curve",names,new Vector3[1],turns,out _,out _),"Mismatched arrays");
Rejected(() => api.TryCreateGhostPlan(p,"Curve",null,poses,turns,out _,out _),"Null array");
Rejected(() => api.TryCreateGhostPlan(p,"Curve",new[]{"wood_beam","missing"},poses,turns,out _,out _),"Unavailable final piece rejects entire batch");
Rejected(() => api.TryCreateGhostPlan(p,"Curve",new[]{"wood_beam","piece_bo_bridge"},poses,turns,out _,out _),"Menu-only bridge rejected");
Rejected(() => api.TryCreateGhostPlan(p,"Curve",new[]{"wood_beam","terrain"},poses,turns,out _,out _),"Terrain-operation pieces rejected");
Rejected(() => api.TryCreateGhostPlan(p,"Curve",names,new[]{poses[0],new Vector3(float.NaN,0,0)},turns,out _,out _),"Nonfinite final pose");
Rejected(() => api.TryCreateGhostPlan(p,"Curve",names,new[]{poses[0],new Vector3(81,0,0)},turns,out _,out _),"Reach bound");
Rejected(() => api.TryCreateGhostPlan(p,"Curve",names,poses,new[]{turns[0],new Quaternion()},out _,out _),"Zero quaternion");
Rejected(() => api.TryCreateGhostPlan(p,"Curve",names,poses,new[]{turns[0],new Quaternion(0,0,float.PositiveInfinity,1)},out _,out _),"Nonfinite quaternion");
PrivateArea.BlockX = 3;
Rejected(() => api.TryCreateGhostPlan(p,"Curve",names,poses,turns,out _,out _),"Final protected piece rejects entire batch");
Rejected(() => api.TryRemoveGhostPlan(p,second,out _,out _),"Removal checks entire group before deletion");
PrivateArea.BlockX = float.NaN;
Location.BlockX = 3; Rejected(() => Create(out _,out _),"No-build ground"); Location.BlockX = float.NaN;
p.Known = false; Rejected(() => Create(out _,out _),"Recipe unlock"); p.Known = true;
api.Enabled = false; Rejected(() => Create(out _,out _),"Disabled planner"); api.Enabled = true;
Rejected(() => api.TryCreateGhostPlan(new Player(),"Curve",names,poses,turns,out _,out _),"Remote player cannot call local API");
api.WorldReady = false; Rejected(() => Create(out _,out _),"Unavailable world"); api.WorldReady = true;
api.World = 2;
Check(Create(out string otherWorld,out _) && api.Orders.Count == 2, "World loads before accepting a new plan");
Check(api.TryRemoveGhostPlan(p,second,out removed,out _) && removed == 0 && api.Orders.Count == 2, "Old-world key cannot remove new-world ghosts");
// Selection uses the current world and fresh ray, never cached Aimed or input processing.
api.Orders.Clear();
api.Orders["far"] = new Plugin.Order {Id="far",Prefab="wood_beam",Pos=new Vector3(0,0,8),Rot=turns[0]};
api.Orders["near"] = new Plugin.Order {Id="near",Prefab="wood_beam",Pos=new Vector3(0,0,3),Rot=turns[0]};
bool Ray(out string id) => api.TryGetGhostAtRay(p,new Vector3(0,0,0),new Vector3(0,0,2),out id,out _,out _,out _,out _);
saved=api.Saves;sent=api.Sent.Count;
Check(Ray(out string selected) && selected=="near","Closest ghost ray intersection selected, direction normalized");
Check(api.Saves==saved && api.Sent.Count==sent && api.Orders.Count==2,"Selection does not save, broadcast, delete, or build");
api.ShowGhosts=false;Check(!Ray(out _),"Hidden ghosts excluded immediately before their render objects are destroyed");api.ShowGhosts=true;
api.Hidden.Add("near");Check(Ray(out selected)&&selected=="far","Unrendered ghost excluded");
api.Orders.Remove("far");Check(!Ray(out _) ,"Disappeared ghost not returned from stale cached aim");
api.Hidden.Clear();
Check(!api.TryGetGhostAtRay(p,new Vector3(float.NaN,0,0),new Vector3(0,0,1),out _,out _,out _,out _,out _),"Nonfinite ray rejected");
Check(!api.TryGetGhostAtRay(p,new Vector3(21,0,0),new Vector3(0,0,1),out _,out _,out _,out _,out _),"Camera origin bounded near local player");
Check(!api.TryGetGhostAtRay(p,new Vector3(),new Vector3(),out _,out _,out _,out _,out _),"Zero ray rejected");
foreach(Action<bool> busy in new Action<bool>[] {v=>api.Placing=v,v=>api.DrawingBridge=v,v=>api.PlansWindowOpen=v,v=>api.BridgeOptionsOpen=v})
{
 busy(true);Check(!api.IsPlanningInputAvailable(p)&&!Ray(out _),"Conflicting planner tool refuses add-on input/query");
 before=api.Orders.Count;saved=api.Saves;sent=api.Sent.Count;
 Rejected(()=>Create(out _,out _),"Creation refuses concurrent blueprint/bridge placement");busy(false);
}
api.Typing=true;Check(!api.IsPlanningInputAvailable(p),"Typing yields input");api.Typing=false;
InventoryGui.Open=true;Check(!api.IsPlanningInputAvailable(p),"Inventory yields input");InventoryGui.Open=false;
Check(api.IsPlanningInputAvailable(p),"Input available after conflicting mode closes");
api.World=3;Check(!Ray(out _)&&api.Orders.Count==0,"Ghost query loads current world before selecting");
Console.WriteLine($"Passed {checks} ghost-only planning API boundary checks (Unity runtime is stubbed).");
