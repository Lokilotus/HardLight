using System.Linq;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Components;
using Content.Shared.CCVar;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests.Station;

[TestFixture]
[TestOf(typeof(EmergencyShuttleSystem))]
public sealed class EvacShuttleTest
{
    /// <summary>
    /// Ensure that the emergency shuttle can be called, and that it will travel to Colcomm
    /// </summary>
    [Test]
    [Ignore("We don't need emergency shuttles.")] // Frontier
    public async Task EmergencyEvacTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();

        // Dummy ticker tests should not have Colcomm
        Assert.That(entMan.Count<StationColcommComponent>(), Is.Zero);

        Assert.That(pair.Server.CfgMan.GetCVar(CCVars.GridFill), Is.False);
        pair.Server.CfgMan.SetCVar(CCVars.EmergencyShuttleEnabled, true);
        pair.Server.CfgMan.SetCVar(CCVars.GameDummyTicker, false);
        var gameMap = pair.Server.CfgMan.GetCVar(CCVars.GameMap);
        pair.Server.CfgMan.SetCVar(CCVars.GameMap, "Saltern");

        await server.WaitPost(() => ticker.RestartRound());
        await pair.RunTicksSync(25);
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

        // Find the station, Colcomm, and shuttle, and ftl map.

        Assert.That(entMan.Count<StationColcommComponent>(), Is.EqualTo(1));
        Assert.That(entMan.Count<StationEmergencyShuttleComponent>(), Is.EqualTo(1));
        Assert.That(entMan.Count<StationDataComponent>(), Is.EqualTo(1));
        Assert.That(entMan.Count<EmergencyShuttleComponent>(), Is.EqualTo(1));
        Assert.That(entMan.Count<FTLMapComponent>(), Is.EqualTo(0));

        var station = (Entity<StationColcommComponent>) entMan.AllComponentsList<StationColcommComponent>().Single();
        var data = entMan.GetComponent<StationDataComponent>(station);
        var shuttleData = entMan.GetComponent<StationEmergencyShuttleComponent>(station);

        var saltern = data.Grids.Single();
        Assert.That(entMan.HasComponent<MapGridComponent>(saltern));

        var shuttle = shuttleData.EmergencyShuttle!.Value;
        Assert.That(entMan.HasComponent<EmergencyShuttleComponent>(shuttle));
        Assert.That(entMan.HasComponent<MapGridComponent>(shuttle));

        var Colcomm = station.Comp.Entity!.Value;
        Assert.That(entMan.HasComponent<MapGridComponent>(Colcomm));

        var ColcommMap = station.Comp.MapEntity!.Value;
        Assert.That(entMan.HasComponent<MapComponent>(ColcommMap));
        Assert.That(server.Transform(Colcomm).MapUid, Is.EqualTo(ColcommMap));

        var salternXform = server.Transform(saltern);
        Assert.That(salternXform.MapUid, Is.Not.Null);
        Assert.That(salternXform.MapUid, Is.Not.EqualTo(ColcommMap));

        var shuttleXform = server.Transform(shuttle);
        Assert.That(shuttleXform.MapUid, Is.Not.Null);
        Assert.That(shuttleXform.MapUid, Is.EqualTo(ColcommMap));

        // All of these should have been map-initialized.
        var mapSys = entMan.System<SharedMapSystem>();
        Assert.That(mapSys.IsInitialized(ColcommMap), Is.True);
        Assert.That(mapSys.IsInitialized(salternXform.MapUid), Is.True);
        Assert.That(mapSys.IsPaused(ColcommMap), Is.False);
        Assert.That(mapSys.IsPaused(salternXform.MapUid!.Value), Is.False);

        EntityLifeStage LifeStage(EntityUid uid) => entMan.GetComponent<MetaDataComponent>(uid).EntityLifeStage;
        Assert.That(LifeStage(saltern), Is.EqualTo(EntityLifeStage.MapInitialized));
        Assert.That(LifeStage(shuttle), Is.EqualTo(EntityLifeStage.MapInitialized));
        Assert.That(LifeStage(Colcomm), Is.EqualTo(EntityLifeStage.MapInitialized));
        Assert.That(LifeStage(ColcommMap), Is.EqualTo(EntityLifeStage.MapInitialized));
        Assert.That(LifeStage(salternXform.MapUid.Value), Is.EqualTo(EntityLifeStage.MapInitialized));

        // Set up shuttle timing
        var shuttleSys = server.System<ShuttleSystem>();
        var evacSys = server.System<EmergencyShuttleSystem>();
        evacSys.TransitTime = shuttleSys.DefaultTravelTime; // Absolute minimum transit time, so the test has to run for at least this long
        // TODO SHUTTLE fix spaghetti

        var dockTime = server.CfgMan.GetCVar(CCVars.EmergencyShuttleDockTime);
        server.CfgMan.SetCVar(CCVars.EmergencyShuttleDockTime, 2);

        // Call evac shuttle.
        await pair.WaitCommand("callshuttle 0:02");
        await pair.RunSeconds(3);

        // Shuttle should have arrived on the station
        Assert.That(shuttleXform.MapUid, Is.EqualTo(salternXform.MapUid));

        await pair.RunSeconds(2);

        // Shuttle should be FTLing back to Colcomm
        Assert.That(entMan.Count<FTLMapComponent>(), Is.EqualTo(1));
        var ftl = (Entity<FTLMapComponent>) entMan.AllComponentsList<FTLMapComponent>().Single();
        Assert.That(entMan.HasComponent<MapComponent>(ftl));
        Assert.That(ftl.Owner, Is.Not.EqualTo(ColcommMap));
        Assert.That(ftl.Owner, Is.Not.EqualTo(salternXform.MapUid));
        Assert.That(shuttleXform.MapUid, Is.EqualTo(ftl.Owner));

        // Shuttle should have arrived at Colcomm
        await pair.RunSeconds(shuttleSys.DefaultTravelTime);
        Assert.That(shuttleXform.MapUid, Is.EqualTo(ColcommMap));

        // Round should be ending now
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PostRound));

        server.CfgMan.SetCVar(CCVars.EmergencyShuttleDockTime, dockTime);
        pair.Server.CfgMan.SetCVar(CCVars.EmergencyShuttleEnabled, false);
        pair.Server.CfgMan.SetCVar(CCVars.GameMap, gameMap);
        await pair.CleanReturnAsync();
    }
}
