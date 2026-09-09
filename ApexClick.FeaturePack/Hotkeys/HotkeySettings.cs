using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace ApexClick.FeaturePack.Hotkeys;
public sealed record HotkeyBinding(string Action,uint Modifiers,uint VirtualKey,string Display);
public sealed class HotkeySettingsStore
{
    private readonly string _path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"ApexClick","hotkeys.json");
    public event EventHandler? Changed;
    public IReadOnlyList<HotkeyBinding> Load(){try{return JsonSerializer.Deserialize<List<HotkeyBinding>>(File.ReadAllText(_path))??Default();}catch{return Default();}}
    public void Save(IEnumerable<HotkeyBinding> values){Directory.CreateDirectory(Path.GetDirectoryName(_path)!);File.WriteAllText(_path,JsonSerializer.Serialize(values,new JsonSerializerOptions{WriteIndented=true})); Changed?.Invoke(this, EventArgs.Empty);}
    private static List<HotkeyBinding> Default()=>new(){new("Record",0,0x75,"F6"),new("Play",0,0x74,"F5")};
}
