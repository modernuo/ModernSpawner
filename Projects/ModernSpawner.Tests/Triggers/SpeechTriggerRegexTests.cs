using System;
using System.Text;
using Server.Engines.ModernSpawner.Triggers;
using Server.Mobiles;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Triggers;

/// <summary>
/// A speech trigger's regex is compiled at registration, and registration runs from world load. An
/// invalid pattern is a configuration mistake, so it must be logged and ignored rather than thrown out of
/// <see cref="ModernSpawner.EnsureTriggersActive" />, where it would abort the load and leave a
/// half-activated trigger set behind.
/// </summary>
[Collection("Sequential ModernSpawner Tests")]
public class SpeechTriggerRegexTests
{
    private static string Definition(string pattern)
    {
        // speech:keyword:ignoreCase:useRegex:range:playersOnly:cooldownSeconds
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(pattern));
        return $"speech:{encoded}:true:true:10:true:0";
    }

    private static ModernSpawner Place(string definition)
    {
        var spawner = new ModernSpawner(1, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), 0, default, "Rabbit");
        spawner.MoveToWorld(new Point3D(1500, 1500, 0), Map.Felucca);
        spawner.AddTriggerDefinition(definition);
        spawner.TriggerActivated = true;
        return spawner;
    }

    private static PlayerMobile PlacePlayer(Point3D at)
    {
        var player = new PlayerMobile { Name = "Talker", Player = true };
        player.MoveToWorld(at, Map.Felucca);
        return player;
    }

    [Fact]
    public void InvalidPattern_RegistersWithoutThrowingAndNeverMatches()
    {
        // "(unclosed" is not a valid regex: Regex's constructor throws ArgumentException on it.
        var spawner = Place(Definition("(unclosed"));
        var player = PlacePlayer(new Point3D(1503, 1500, 0));

        try
        {
            Assert.True(TriggerSystem.Instance.IsRegistered(spawner));
            Assert.True(spawner.HandlesOnSpeech);

            TriggerSystem.Instance.OnSpeech(player, "(unclosed", player.Location, player.Map, spawner);
            TriggerSystem.Instance.OnSpeech(player, "anything at all", player.Location, player.Map, spawner);

            Assert.Equal(0, spawner.PendingCycleCount);
        }
        finally
        {
            player.Delete();
            spawner.Delete();
        }
    }

    [Fact]
    public void ValidPattern_StillMatches()
    {
        var spawner = Place(Definition("^hail"));
        var player = PlacePlayer(new Point3D(1503, 1500, 0));

        try
        {
            TriggerSystem.Instance.OnSpeech(player, "nothing here", player.Location, player.Map, spawner);
            Assert.Equal(0, spawner.PendingCycleCount);

            TriggerSystem.Instance.OnSpeech(player, "hail friend", player.Location, player.Map, spawner);
            Assert.Equal(1, spawner.PendingCycleCount);
        }
        finally
        {
            player.Delete();
            spawner.Delete();
        }
    }
}
