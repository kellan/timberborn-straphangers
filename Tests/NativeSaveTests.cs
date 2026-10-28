using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Timberborn.Persistence;
using Timberborn.SerializationSystem;
using Timberborn.WorldPersistence;
using BoardingCounter;
public static class NativeSaveTests
{
 // Only the singleton registry is in memory. Strings are written/read using
 // the actual game's ObjectSaver/ObjectLoader and SerializedObject classes.
 sealed class GameSave : ISingletonLoader,ISingletonSaver {
  public readonly Dictionary<string,SerializedObject> Data=new Dictionary<string,SerializedObject>();
  public IObjectLoader GetSingleton(SingletonKey key)=>new ObjectLoader(Data[key.Name]);
  public bool TryGetSingleton(SingletonKey key,out IObjectLoader value){if(Data.TryGetValue(key.Name,out var data)){value=new ObjectLoader(data);return true;}value=null!;return false;}
  IObjectSaver ISingletonSaver.GetSingleton(SingletonKey key){if(!Data.TryGetValue(key.Name,out var data))Data[key.Name]=data=new SerializedObject();return new ObjectSaver(data);}
 }
 public static void Run(Action<bool,string> check) {
  Func<object,string> serialize=o=>JsonConvert.SerializeObject(o);
  var save=new GameSave();var store=new RidershipSaveStore(save,serialize);var counts=store.Load(100);
  check(!store.Restored&&counts.FirstDay==100&&counts.Stations.Count==0,"an unmodified game save starts fresh tracking without importing another session");
  counts.Record("station-guid","tubeway","Station café\nA",100,true,false);counts.Record("station-guid","tubeway","Station café\nA",101,true,true);
  store.Save(save,counts,new[]{"station-guid"},102);
  var reloadStore=new RidershipSaveStore(save,serialize);var restored=reloadStore.Load(102);
  check(reloadStore.Restored&&restored.Stations["station-guid"].Name=="Station café\nA"&&restored.Stations["station-guid"].TotalBoardings==2&&restored.Average("station-guid",102)==1,"native game save objects and the production JSON loader restore totals, Unicode names and averages");
  var separate=new RidershipSaveStore(new GameSave(),serialize).Load(102);check(separate.Stations.Count==0,"native singleton storage keeps another save isolated");
  restored.Record("station-guid","tubeway","Station café\nA",102,true,false);reloadStore.Save(save,restored,new[]{"station-guid"},102);
  check(new RidershipSaveStore(save,serialize).Load(102).Stations["station-guid"].TotalBoardings==3,"a native save after reloading continues the same station total");
  var corrupt=new GameSave();var key=new SingletonKey("Kellan.BoardingCounter.Ridership");var stateKey=new PropertyKey<string>("State");((ISingletonSaver)corrupt).GetSingleton(key).Set(stateKey,"{\"schema\":999}");
  var badStore=new RidershipSaveStore(corrupt,serialize);bool failed=false;try{badStore.Load(102);}catch(InvalidDataException){failed=true;}
  badStore.Save(corrupt,null,Array.Empty<string>(),102);
  check(failed&&corrupt.GetSingleton(key).Get(stateKey)=="{\"schema\":999}","an unreadable saved version fails explicitly and is preserved rather than replaced with zeros");
 }
}
