using System.Text.Json;
using Genesis.Engine.Core;
namespace Genesis.Engine.Core.Tests;
public class ReplayTests
{
    [Fact]
    public void SavedWorldContinuesIdenticallyIncludingBirthsAndJournal()
    {
        var original = new Simulation(24,24,73);
        for(int i=0;i<150;i++) original.Step(.05f);
        var restored=Simulation.LoadJson(original.SaveJson());
        Assert.Equal(original.SaveJson(),restored.SaveJson());
        for(int i=0;i<300;i++)
        {
            if(i%50==0) { original.SpawnAgent(new(12,12)); restored.SpawnAgent(new(12,12)); }
            original.Step(.05f); restored.Step(.05f);
        }
        Assert.Equal(original.SaveJson(),restored.SaveJson());
    }
    [Fact]
    public void FileSaveReplacesCompleteSnapshotAndCleansTemporaryFiles()
    {
        string directory=Path.Combine(Path.GetTempPath(),"genesis-test-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(directory,"world.json");
        try
        {
            var simulation=new Simulation(8,8);
            WorldFile.Save(simulation,path);
            simulation.Step(.1f);
            WorldFile.Save(simulation,path);
            Assert.Equal(simulation.SaveJson(),WorldFile.Load(path).SaveJson());
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
    }

    [Fact]
    public void LegacySavesLoadAndMalformedReplayStateIsRejected()
    {
        var data=JsonSerializer.Deserialize<WorldSave>(new Simulation().SaveJson())!;
        data.Version=1; data.RandomState=null; data.Events=null;
        Assert.NotEmpty(Simulation.LoadJson(JsonSerializer.Serialize(data)).Agents);
        data.Version=2;
        Assert.Throws<ArgumentException>(()=>Simulation.LoadJson(JsonSerializer.Serialize(data)));
        data.RandomState=0;data.Events=new string[65];
        Assert.Throws<ArgumentException>(()=>Simulation.LoadJson(JsonSerializer.Serialize(data)));
    }
}
