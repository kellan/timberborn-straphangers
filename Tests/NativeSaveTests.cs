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
  var legacy=new GameSave();var legacyKey=new SingletonKey("Kellan.BoardingCounter.Ridership");var legacyStateKey=new PropertyKey<string>("State");
  ((ISingletonSaver)legacy).GetSingleton(legacyKey).Set(legacyStateKey,"{\"schema\":1,\"first_day\":100,\"stations\":[{\"id\":\"old\",\"mode\":\"tubeway\",\"name\":\"Old\",\"first_day\":100,\"entries\":2,\"exits\":1,\"bot_entries\":0,\"days\":[{\"day\":101,\"entries\":1,\"exits\":1}]}]}");
  var legacyStore=new RidershipSaveStore(legacy,serialize);var legacyCounts=legacyStore.Load(102);
  check(legacyCounts.Stations["old"].TotalBoardings==2&&legacyCounts.Stations["old"].TotalArrivals==1&&legacyCounts.Stations["old"].HourlyFirstDay==null,"native JSON loading migrates an actual schema-one field layout without inventing hourly history");
  legacyCounts.BeginHourlyTracking("old",102,.4);legacyCounts.Record("old","tubeway","Old",102,true,false,.5);legacyCounts.Record("old","tubeway","Old",102,false,false,.5);
  legacyStore.Save(legacy,legacyCounts,new[]{"old"},102);
  var upgraded=new RidershipSaveStore(legacy,serialize).Load(102);
  check(upgraded.Stations["old"].TotalBoardings==3&&upgraded.Stations["old"].TotalArrivals==2&&upgraded.Stations["old"].HourlyDays[102].Boardings[12]==1&&upgraded.Stations["old"].HourlyDays[102].Alightings[12]==1,"the native save path preserves legacy totals and new hourly directions together");
  var corrupt=new GameSave();var key=new SingletonKey("Kellan.BoardingCounter.Ridership");var stateKey=new PropertyKey<string>("State");((ISingletonSaver)corrupt).GetSingleton(key).Set(stateKey,"{\"schema\":999}");
  var badStore=new RidershipSaveStore(corrupt,serialize);bool failed=false;try{badStore.Load(102);}catch(InvalidDataException){failed=true;}
  badStore.Save(corrupt,null,Array.Empty<string>(),102);
  check(failed&&corrupt.GetSingleton(key).Get(stateKey)=="{\"schema\":999}","an unreadable saved version fails explicitly and is preserved rather than replaced with zeros");
 }
}
