using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

public class GamaPreviewReuseCaptureTests
{
    [Test]
    public void Accumulator_PreservesEveryAgentWhenStableIdentityIsDuplicated()
    {
        Type accumulatorType = typeof(GamaPanelWindow).Assembly.GetType(
            "GamaEditorPreviewWorldAccumulator",
            true);
        object accumulator = Activator.CreateInstance(accumulatorType, true);
        MethodInfo merge = accumulatorType.GetMethod(
            "Merge",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo toWorldJson = accumulatorType.GetMethod(
            "ToWorldJson",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.That(merge, Is.Not.Null);
        Assert.That(toWorldJson, Is.Not.Null);

        string chunkJson = @"{
  ""names"": [""alice"", ""bob""],
  ""keepNames"": [""alice"", ""bob""],
  ""propertyID"": [""people"", ""people""],
  ""pointsLoc"": [
    { ""c"": [10, 20, 0] },
    { ""c"": [30, 40, 0] }
  ],
  ""pointsGeom"": [],
  ""offsetYGeom"": [],
  ""attributes"": [
    { ""id"": ""shared-id"" },
    { ""id"": ""shared-id"" }
  ],
  ""ranking"": [0, 1]
}";

        Type jsonObjectType = merge.GetParameters()[0].ParameterType;
        MethodInfo parseJson = jsonObjectType.GetMethod(
            "Parse",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(string) },
            null);
        Assert.That(parseJson, Is.Not.Null);
        object chunk = parseJson.Invoke(null, new object[] { chunkJson });

        Dictionary<string, PropertiesGAMA> propertyMap =
            new Dictionary<string, PropertiesGAMA>(StringComparer.Ordinal)
            {
                ["people"] = new PropertiesGAMA
                {
                    id = "people",
                    hasPrefab = true,
                    prefab = "People/Person"
                }
            };

        object mergeResult = merge.Invoke(
            accumulator,
            new[] { chunk, (object)12, propertyMap, null });
        FieldInfo cacheAgentCount = mergeResult.GetType().GetField(
            "CacheAgentCount",
            BindingFlags.Instance | BindingFlags.Public);
        Assert.That(cacheAgentCount, Is.Not.Null);
        Assert.That((int)cacheAgentCount.GetValue(mergeResult), Is.EqualTo(2));

        string accumulatedJson = (string)toWorldJson.Invoke(accumulator, null);
        WorldJSONInfo accumulatedWorld = WorldJSONInfo.CreateFromJSON(accumulatedJson);

        Assert.That(accumulatedWorld, Is.Not.Null);
        Assert.That(accumulatedWorld.names, Is.EqualTo(new[] { "alice", "bob" }));
        Assert.That(accumulatedWorld.propertyID, Is.EqualTo(new[] { "people", "people" }));
        Assert.That(accumulatedWorld.attributes, Has.Count.EqualTo(2));

        Assert.That(
            accumulatedWorld.attributes[0].TryGetString(out string firstId, "id"),
            Is.True);
        Assert.That(
            accumulatedWorld.attributes[1].TryGetString(out string secondId, "id"),
            Is.True);
        Assert.That(firstId, Is.EqualTo("shared-id"));
        Assert.That(secondId, Is.EqualTo("shared-id"));
    }
}
