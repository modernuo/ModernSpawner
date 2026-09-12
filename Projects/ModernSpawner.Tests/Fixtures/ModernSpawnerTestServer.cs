using System;
using System.Reflection;
using System.Threading;
using Server.Engines.ModernSpawner.Scripting;
using Server.Items;
using Server.Misc;
using Server.Movement;
using Server.Tests.Maps;

namespace Server.Engines.ModernSpawner.Tests.Fixtures;

/// <summary>
/// Single, process-wide ModernUO bootstrap for the ModernSpawner test host. Modelled on
/// ModernUO's <c>UOContent.Tests</c> fixture of the same shape, which is <c>internal</c> to that
/// assembly and so cannot be reused directly; the map registrations are shared rather than copied.
///
/// ModernUO bootstraps its global singletons (Core, ServerConfiguration, AssemblyHandler,
/// NetState, World, Timer and the serialization workers) exactly once per process, and
/// <see cref="World.Load" /> is guarded to run once. Each xUnit collection fixture instance
/// calls <see cref="Initialize" />, so the guard here keeps the bootstrap to a single run.
///
/// Anything that needs the copyrighted UO client files (tile data, multi data, map tiles) is
/// deliberately skipped: it is absent on CI and no ModernSpawner test depends on it.
/// </summary>
public static class ModernSpawnerTestServer
{
    private static readonly Lock _lock = new();
    private static bool _initialized;

    public static void Initialize()
    {
        lock (_lock)
        {
            if (_initialized)
            {
                return;
            }

            Core.ApplicationAssembly = Assembly.GetExecutingAssembly();
            Core.LoopContext = new EventLoopContext();
            Core.Expansion = Expansion.EJ;

            ServerConfiguration.Load(true);
            ServerConfiguration.AssemblyDirectories.Add(Core.BaseDirectory);

            AssemblyHandler.LoadAssemblies(["Server.dll", "UOContent.dll", "ModernSpawner.dll"]);

            SkillsInfo.Configure();

            // Production (Main.cs) and ModernUO's own fixtures seed the loop clock here; Server.dll
            // grants this assembly InternalsVisibleTo, so the same seam is available. Without it
            // Core.Now stays DateTime.MinValue and anything comparing against an absolute wall clock
            // (cooldowns, time windows) reads as "never elapsed".
            Core._now = DateTime.UtcNow;

            // The timer wheel must exist before NetState.Configure(), which schedules a recurring
            // sweep through Timer.DelayCall (production order in Main.cs: Timer.Init runs before
            // AssemblyHandler.Invoke("Configure")).
            Timer.Init(0);
            Server.Network.NetState.Configure();

            // Reuses ModernUO's own test map registrations (Server.Tests), so the map table here
            // cannot drift from the engine's.
            TestMapDefinitions.ConfigureTestMapDefinitions();

            World.Configure();
            // Registers the Accounts entity persistence; without it no test can construct an Account.
            Server.Accounting.Accounts.Configure();
            RaceDefinitions.Configure();
            Server.Movement.Movement.Configure();
            MovementImpl.Configure();
            PathFollower.Configure();

            // ModernSpawner persistence and registries must exist before World.Load(): ScriptRegistry
            // is a GenericPersistence and registers itself with World from its constructor.
            ScriptRegistry.Configure();
            ModernSpawnerConfiguration.Configure();

            World.Load();
            World.ExitSerializationThreads();

            DecayScheduler.Configure();
            // WallTimeWindowTrigger.Activate schedules through EventScheduler.Shared, which is null
            // until this runs (production reaches it through UOContent's Configure pass).
            Server.Engines.Events.EventScheduler.Configure();
            // Without npc-speeds.json every BaseCreature constructor throws.
            Server.Mobiles.NPCSpeeds.Configure();
            Server.Engines.Spawners.SpawnerJsonSerializer.Configure();

            _initialized = true;
        }
    }

    /// <summary>
    /// Moves the engine clock forward. Only valid in this host, which never ticks the timer wheel, so
    /// nothing schedules off the value being advanced.
    /// </summary>
    /// <param name="by">How far forward to move <see cref="Core.Now" />.</param>
    public static void AdvanceClock(TimeSpan by) => Core._now += by;
}
