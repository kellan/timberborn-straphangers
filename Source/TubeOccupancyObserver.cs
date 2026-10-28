using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.EntitySystem;
using Timberborn.TubeSystem;
namespace BoardingCounter;

// Subscribe to the same membership event as TubeIlluminator, without patches.
// Tube is internal, so only its fixed read-only fields/events use reflection.
public sealed class TubeOccupancyObserver : IDisposable
{
 readonly EntityRegistry entities;
 readonly Action<string,int[],string,ReplayOccupant[]> emit;
 readonly Action<Exception> error;
 readonly Type tubeType;
 readonly FieldInfo beavers,bots;
 readonly EventInfo changed,deleted;
 readonly Dictionary<Guid,Watch> watches=new Dictionary<Guid,Watch>();
 bool disposed;
 public int Count=>watches.Count;
 public TubeOccupancyObserver(EntityRegistry entities,Action<string,int[],string,ReplayOccupant[]> emit,Action<Exception> error) {
  this.entities=entities;this.emit=emit;this.error=error;
  tubeType=typeof(TubeSpec).Assembly.GetType("Timberborn.TubeSystem.Tube",true)!;
  beavers=Field("_beaverVisitors");bots=Field("_botVisitors");changed=Event("VisitorsChanged");deleted=Event("TubeDeleted");
 }
 FieldInfo Field(string name){var f=tubeType.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)??throw new MissingFieldException(tubeType.FullName,name);if(!typeof(IEnumerable).IsAssignableFrom(f.FieldType)||!f.IsInitOnly)throw new InvalidOperationException("Tube visitor set shape changed");return f;}
 EventInfo Event(string name){var e=tubeType.GetEvent(name)??throw new MissingMemberException(tubeType.FullName,name);if(e.EventHandlerType!=typeof(EventHandler))throw new InvalidOperationException("Tube event shape changed");return e;}
 public void Refresh() {
  if(disposed)return;
  try {
   var seen=new HashSet<Guid>();
   foreach(var entity in entities.Entities) {
    if(entity.Deleted||!entity.HasComponent<TubeSpec>()||!entity.TryGetComponent<BlockObject>(out var block)||!block.IsFinished)continue;
    seen.Add(entity.EntityId);if(watches.ContainsKey(entity.EntityId))continue;
    var tube=entity.AllComponents.Single(c=>c.GetType()==tubeType);
    var w=new Watch(this,tube,entity.EntityId,new[]{block.Coordinates.x,block.Coordinates.y,block.Coordinates.z});
    watches.Add(entity.EntityId,w);w.Start();
   }
   foreach(var id in watches.Keys.Where(id=>!seen.Contains(id)).ToArray())Remove(id);
  }catch(Exception e){error(e);}
 }
 void Remove(Guid id){if(watches.TryGetValue(id,out var w)){w.Detach();watches.Remove(id);emit(id.ToString(),w.Grid,"remove",Array.Empty<ReplayOccupant>());}}
 ReplayOccupant[] Occupants(object tube) {
  var result=new List<ReplayOccupant>();
  foreach(var pair in new[]{(beavers,false),(bots,true)})foreach(var visitor in (IEnumerable)pair.Item1.GetValue(tube)!) {
   var component=visitor as BaseComponent??throw new InvalidOperationException("Tube visitor is not a component");
   result.Add(new ReplayOccupant{id=component.GetComponent<EntityComponent>().EntityId.ToString(),bot=pair.Item2});
  }
  return result.ToArray();
 }
 sealed class Watch {
  readonly TubeOccupancyObserver owner;readonly object tube;readonly Guid id;
  public readonly int[] Grid;
  public Watch(TubeOccupancyObserver owner,object tube,Guid id,int[] grid){this.owner=owner;this.tube=tube;this.id=id;Grid=grid;}
  public void Start(){owner.changed.AddEventHandler(tube,new EventHandler(Change));owner.deleted.AddEventHandler(tube,new EventHandler(Delete));owner.emit(id.ToString(),Grid,"baseline",owner.Occupants(tube));}
  void Change(object sender,EventArgs args){if(owner.disposed)return;try{owner.emit(id.ToString(),Grid,"change",owner.Occupants(tube));}catch(Exception e){owner.error(e);}}
  void Delete(object sender,EventArgs args){if(owner.disposed)return;try{owner.Remove(id);}catch(Exception e){owner.error(e);}}
  public void Detach(){owner.changed.RemoveEventHandler(tube,new EventHandler(Change));owner.deleted.RemoveEventHandler(tube,new EventHandler(Delete));}
 }
 public void Dispose(){if(disposed)return;disposed=true;foreach(var w in watches.Values)w.Detach();watches.Clear();}
}
