using CigarSmoking;
int checks=0;
void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
var slots=new SmokingSlots();
Check(!slots.Register(null)&&!slots.Register("")&&!slots.Register(new string('x',129)),"Bounded nonempty names");
Check(slots.Register("pipe")&&slots.Register("pipe"),"Idempotent registration");
var player=new Character();
void Add(params string[] effects){foreach(string name in effects)player.Effects.Active.Add(name.GetStableHashCode());}
bool Has(string name)=>player.Effects.Active.Contains(name.GetStableHashCode());
Add("cigar_meadow","cigar_plains","pipe","rested");
Check(slots.StopOthers(player,"pipe")&&!Has("cigar_meadow")&&!Has("cigar_plains")&&Has("pipe")&&Has("rested"),"Pipe stops cigars and preserves itself and unrelated buffs");
Add("cigar_meadow");
Check(slots.StopOthers(player,"cigar_meadow")&&Has("cigar_meadow")&&!Has("pipe")&&Has("rested"),"Cigar stops pipe in reverse direction");
Check(!slots.StopOthers(player,"unregistered")&&Has("cigar_meadow"),"Invalid keep name cannot remove effects");
Check(!slots.StopOthers(null,"pipe"),"Missing character");
slots.Unregister("pipe");Add("pipe");slots.StopOthers(player,"cigar_meadow");Check(Has("pipe"),"Unloaded add-on is no longer managed");
Check(!new SmokingSlots().StopOthers(player,"pipe"),"Reloaded plugin needs fresh registration");
slots.Register("pipe");slots.Register("other_pipe");Add("pipe","other_pipe");
player.Effects.OnRemove=()=>slots.Unregister("other_pipe");
Check(slots.StopOthers(player,"cigar_meadow")&&!Has("pipe")&&!Has("other_pipe"),"Stopping callbacks may unregister without invalidating enumeration");
slots=new SmokingSlots();for(int i=0;i<32;i++)Check(slots.Register("addon_"+i),"Within registration capacity");
Check(!slots.Register("overflow")&&slots.Register("addon_0"),"Capacity still allows idempotent lookups");
slots.Unregister("addon_0");Check(slots.Register("replacement"),"Unregister releases capacity");
Console.WriteLine($"Smoking slots: {checks} checks passed");

public class Character{public SEMan Effects=new();public SEMan GetSEMan()=>Effects;}
public class SEMan
{
 public HashSet<int> Active=new();public Action OnRemove;
 public bool HaveStatusEffect(int hash)=>Active.Contains(hash);
 public bool RemoveStatusEffect(int hash,bool quiet){bool found=Active.Remove(hash);if(found)OnRemove?.Invoke();return found;}
}
public static class Hashes
{
 public static int GetStableHashCode(this string name){unchecked{int hash=17;foreach(char c in name)hash=hash*31+c;return hash;}}
}
namespace UnityEngine{}
namespace CigarSmoking
{
 public static class Plugin{public const string EffectPrefix="cigar_";}
 public class CigarType{public string Id;}
 public static class Types{public static CigarType[] All={new(){Id="meadow"},new(){Id="plains"}};}
}
