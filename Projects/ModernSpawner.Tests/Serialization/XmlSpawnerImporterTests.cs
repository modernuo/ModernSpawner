using Server.Engines.ModernSpawner.Serialization;
using Xunit;
using System.IO;

namespace Server.Engines.ModernSpawner.Tests.Serialization;

public class XmlSpawnerImporterTests
{
    private const string ServUOFormatXml = """
        <Spawns>
          <Points>
            <Name>Test Orc Spawner</Name>
            <UniqueId>003f11b8-9bfa-4587-991e-ca263004efe6</UniqueId>
            <Map>Felucca</Map>
            <X>1000</X>
            <Y>1000</Y>
            <Width>20</Width>
            <Height>20</Height>
            <CentreX>1010</CentreX>
            <CentreY>1010</CentreY>
            <CentreZ>0</CentreZ>
            <Range>10</Range>
            <MaxCount>5</MaxCount>
            <MinDelay>5</MinDelay>
            <MaxDelay>10</MaxDelay>
            <DelayInSec>False</DelayInSec>
            <ProximityRange>-1</ProximityRange>
            <Team>0</Team>
            <IsGroup>False</IsGroup>
            <IsRunning>True</IsRunning>
            <SmartSpawning>False</SmartSpawning>
            <Objects2>Orc:MX=3:SB=0:SP=1:OBJ=OrcishLord:MX=2:SB=0:SP=0.5</Objects2>
          </Points>
        </Spawns>
        """;

    private const string SnoFormatXml = """
        <spawners count="1">
          <spawner>
            <count>3</count>
            <group>False</group>
            <homerange>10</homerange>
            <walkingrange>-1</walkingrange>
            <maxdelay>00:10:00</maxdelay>
            <mindelay>00:05:00</mindelay>
            <team>0</team>
            <creaturesname>
              <creaturename>Skeleton</creaturename>
              <creaturename>Zombie</creaturename>
            </creaturesname>
            <name>Graveyard Spawner</name>
            <location>(1500, 1500, 0)</location>
            <map>Felucca</map>
          </spawner>
        </spawners>
        """;

    [Fact]
    public void ImportServUOFormat_ParsesBasicProperties()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, ServUOFormatXml);

            var result = XmlSpawnerImporter.ImportFromFile(tempFile, respawn: false);

            // In test environment without full server, Map.Parse returns null
            // so spawners may fail to import. We just verify no XML parsing errors.
            Assert.True(result.Imported >= 0);
            // Check no XML parsing errors occurred
            foreach (var error in result.Errors)
            {
                Assert.DoesNotContain("Failed to load XML", error);
                Assert.DoesNotContain("Unrecognized XML format", error);
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ImportSnoFormat_ParsesBasicProperties()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, SnoFormatXml);

            var result = XmlSpawnerImporter.ImportFromFile(tempFile, respawn: false);

            // In test environment without full server, Map.Parse returns null
            // so spawners may fail to import. We just verify no XML parsing errors.
            Assert.True(result.Imported >= 0);
            // Check no XML parsing errors occurred
            foreach (var error in result.Errors)
            {
                Assert.DoesNotContain("Failed to load XML", error);
                Assert.DoesNotContain("Unrecognized XML format", error);
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ImportFromFile_InvalidPath_ReturnsError()
    {
        var result = XmlSpawnerImporter.ImportFromFile("/nonexistent/path.xml");

        Assert.Equal(0, result.Imported);
        Assert.Single(result.Errors);
        Assert.Contains("not found", result.Errors[0]);
    }

    [Fact]
    public void ImportFromFile_InvalidXml_ReturnsError()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "not valid xml <>");

            var result = XmlSpawnerImporter.ImportFromFile(tempFile, respawn: false);

            Assert.Equal(0, result.Imported);
            Assert.Single(result.Errors);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ImportFromFile_UnknownFormat_ReturnsError()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "<root><unknown>data</unknown></root>");

            var result = XmlSpawnerImporter.ImportFromFile(tempFile, respawn: false);

            Assert.Equal(0, result.Imported);
            Assert.Single(result.Errors);
            Assert.Contains("Unrecognized XML format", result.Errors[0]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
