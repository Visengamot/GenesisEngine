using Genesis.Engine.Core;
namespace Genesis.Engine.Core.Tests;
public class SensorBufferTests
{
    [Fact]
    public void BufferedSensorsMatchAndAllocateNothing()
    {
        var simulation=new Simulation();
        var agent=simulation.Agents[0];
        var expected=simulation.Sense(agent);
        Span<float> inputs=stackalloc float[Agent.SensorCount];
        for(int i=0;i<100;i++) simulation.Sense(agent,inputs);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++) simulation.Sense(agent,inputs);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.Equal(expected,inputs.ToArray());
        Assert.Equal(0,allocated);
        Assert.Throws<ArgumentException>(()=>simulation.Sense(agent,new float[1]));
    }
}
