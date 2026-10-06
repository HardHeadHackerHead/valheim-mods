using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public struct Quaternion
    {
        public float x,y,z,w; public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}
        public static float Angle(Quaternion a,Quaternion b) => (float)(2*Math.Acos(Math.Min(1,Math.Abs(a.x*b.x+a.y*b.y+a.z*b.z+a.w*b.w)))*180/Math.PI);
    }
    public class Transform { public Vector3 position; }
    public class GameObject
    {
        public Piece Piece; public TerrainOp Terrain;
        public T GetComponent<T>() where T:class => Piece as T;
        public T GetComponentInChildren<T>(bool includeInactive) where T:class => Terrain as T;
    }
}
public class Piece { public string m_name; }
public class TerrainOp {}
public class TerrainModifier {}
public class Player
{
    public static Player m_localPlayer; public UnityEngine.Transform transform = new(); public bool Known=true;
    public bool IsDead()=>false; public bool IsRecipeKnown(string name)=>Known;
}
public class ZNetScene
{
    public static ZNetScene instance; public Dictionary<string,UnityEngine.GameObject> Prefabs=new();
    public UnityEngine.GameObject GetPrefab(string name)=>Prefabs.GetValueOrDefault(name);
}
public class ObjectDB { public static ObjectDB instance; }
public static class PrivateArea
{
    public static float BlockX=float.NaN;
    public static bool CheckAccess(UnityEngine.Vector3 p,float radius,bool flash,bool wardCheck)=>p.x!=BlockX;
}
public static class Location { public static float BlockX=float.NaN; public static bool IsInsideNoBuildLocation(UnityEngine.Vector3 p)=>p.x==BlockX; }
namespace BuildOrders
{
    public partial class Plugin
    {
        public static Plugin Instance;
        public class Order { public string Id,Prefab,By; public UnityEngine.Vector3 Pos; public UnityEngine.Quaternion Rot; }
        private readonly Dictionary<string,Order> _orders=new(); public Dictionary<string,Order> Orders=>_orders;
        private class Setting { public bool Value=true; } private readonly Setting _enabled=new();
        public bool Enabled {get=>_enabled.Value;set=>_enabled.Value=value;}
        private bool _stabilityDirty; public bool StabilityDirty=>_stabilityDirty; private const string BridgeToolPrefab="piece_bo_bridge";
        public int Saves,World=1; private int _loaded; public bool WorldReady=true;
        public List<string> Sent=new(); public HashSet<string> Records=new(),Built=new(),Terrain=new(){"old terrain snapshot"};
        private bool WorldKnown=>WorldReady&&_loaded==World;
        private void EnsureWorldLoaded(){if(WorldReady&&_loaded!=World){_orders.Clear();_loaded=World;}}
        private HashSet<string> Buildable()=>new(){"wood_beam","piece_bo_bridge","terrain"};
        private void Save()=>Saves++; private void SaveOrders()=>Save();
        private void Send(string message)=>Sent.Add(message);
        private string Encode(Order o)=>o.Id+"|"+o.Prefab;
        private void RecordPlan(string key)=>Records.Add(key);
        private void RemoveOrder(string id,bool broadcast,bool save){_orders.Remove(id);if(broadcast)Send("R|"+id);if(save)Save();}
        // Guard against accidentally adopting blueprint helpers that alter terrain or built objects.
        private void RemovePlan(string key)=>throw new Exception("Terrain-changing RemovePlan called");
        private void ClearStaleRecords(string key)=>throw new Exception("Terrain-changing stale-record cleanup called");
    }
}
