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
Console.WriteLine($"Passed {checks} ghost-only planning API boundary checks (Unity runtime is stubbed).");
