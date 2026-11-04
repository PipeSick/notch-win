using System.Text.Json;
using NotchWin.Core.Activities;
using Xunit;

namespace NotchWin.Core.Tests;

/// <summary>It8: el contrato con WinMac Cleanup. WinMac manda su progreso como actividad de tipo extensión por la API interna de actividades; estos son los mensajes EXACTOS que genera.</summary>
public class CleanupActivityContractTests
{
    private static ActivityFeed.Command? P(string json) => ActivityFeed.Parse(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void Progress_upsert_is_an_extension_activity_with_progress()
    {
        var c = P("{\"op\":\"upsert\",\"activity\":{\"id\":\"winmac-cleanup\",\"kind\":\"extension\",\"title\":\"Limpiando archivos seleccionados\",\"subtitle\":\"Limpiando caché\",\"source\":\"WinMac Cleanup\",\"progress\":0.4,\"state\":\"active\"}}")!;
        Assert.Equal("upsert", c.Op); Assert.Equal("ipc:winmac-cleanup", c.Activity!.Id); Assert.Equal(ActivityKind.Extension, c.Activity.Kind); Assert.Equal(0.4, c.Activity.Progress);
    }

    [Fact]
    public void Finish_states_map_to_completed_and_failed_and_cancelled()
    {
        Assert.Equal(ActivityState.Completed, P("{\"op\":\"finish\",\"activity\":{\"id\":\"winmac-cleanup\",\"state\":\"completed\"}}")!.State);
        Assert.Equal(ActivityState.Failed, P("{\"op\":\"finish\",\"activity\":{\"id\":\"winmac-cleanup\",\"state\":\"failed\"}}")!.State);
        Assert.Equal(ActivityState.Cancelled, P("{\"op\":\"finish\",\"activity\":{\"id\":\"winmac-cleanup\",\"state\":\"cancelled\"}}")!.State);
    }

    [Fact]
    public void Cleanup_activity_never_replaces_a_media_activity_nor_the_islands_own_ids()
    {
        var store = new ActivityStore(() => DateTime.UtcNow);
        store.Upsert(new Activity("media:now", ActivityKind.Media, "Canción") { State = ActivityState.Active });
        ActivityFeed.Apply(store, P("{\"op\":\"upsert\",\"activity\":{\"id\":\"winmac-cleanup\",\"kind\":\"extension\",\"title\":\"Analizando archivos temporales\",\"state\":\"active\"}}")!);
        Assert.NotNull(store.Find("media:now")); Assert.NotNull(store.Find("ipc:winmac-cleanup")); Assert.Equal(2, store.Current.Count);
        ActivityFeed.Apply(store, P("{\"op\":\"remove\",\"activity\":{\"id\":\"winmac-cleanup\"}}")!);
        Assert.NotNull(store.Find("media:now")); Assert.Null(store.Find("ipc:winmac-cleanup"));
    }
}
