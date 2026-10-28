using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Timberborn.Persistence;
using Timberborn.WorldPersistence;
namespace BoardingCounter;

// Native singleton storage makes counts follow the exact save/colony timeline.
public sealed class RidershipSaveStore
{
 static readonly SingletonKey RidershipKey=new SingletonKey("Kellan.BoardingCounter.Ridership");
 static readonly PropertyKey<string> StateKey=new PropertyKey<string>("State");
 readonly ISingletonLoader loader;
 readonly Func<object,string> serialize;
 string? loadedJson;
 public bool Restored {get;private set;}
 public RidershipSaveStore(ISingletonLoader loader,Func<object,string> serialize){this.loader=loader;this.serialize=serialize;}
 public Counter Load(int today) {
  if(!loader.TryGetSingleton(RidershipKey,out var saved))return new Counter(today);
  loadedJson=saved.Get(StateKey);
  var state=JsonConvert.DeserializeObject<CounterState>(loadedJson,new JsonSerializerSettings{TypeNameHandling=TypeNameHandling.None,MissingMemberHandling=MissingMemberHandling.Error});
  var counts=CounterPersistence.Restore(state??throw new InvalidDataException("Empty saved ridership"),today);Restored=true;return counts;
 }
 public void Save(ISingletonSaver saver,Counter? counts,IEnumerable<string> activeStations,int today) {
  // Preserve unreadable data if loading fails, rather than overwrite it with zeros.
  string? json=counts!=null?serialize(CounterPersistence.Capture(counts,activeStations,today)):loadedJson;
  if(json!=null)saver.GetSingleton(RidershipKey).Set(StateKey,json);
 }
}
