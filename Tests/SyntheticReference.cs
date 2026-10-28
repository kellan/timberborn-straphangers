using System.Collections.Generic;
using System.Text.Json;
public static class SyntheticReference
{
 // Minimal synthetic door, not an extracted game blueprint or real map.
 public static string Json() {
  var spec=new {source="constructed-test-door",block=new{Flippable=false,Entrance=new{Coordinates=new{X=1,Y=-1,Z=0}}},navigation=new{EdgeGroups=new[]{new{Group=new{UseGroup=false},AddedEdges=new[]{new{Start=new{X=1,Y=0,Z=0},End=new{X=1,Y=-1,Z=0}}}}}}};
  return JsonSerializer.Serialize(new{schema=1,game_version="constructed-fixture",movement_assembly_sha256=new string('0',64),templates=new Dictionary<string,object>{{"TubewayStation.IronTeeth",spec},{"ZiplineStation.Folktails",spec}}});
 }
}
