using RealmOfThrones.Core;

namespace BannerlordCoopLauncher.Tests;

public class RotPlayerMapTests
{
    [Fact]
    public void An_unreadable_scene_blocks_the_join()
    {
        var module = Directory.CreateTempSubdirectory("rot-player-map-locked-").FullName;
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(module, "SceneObj", "Main_map"));
            var scene = Path.Combine(folder.FullName, "scene.xscene");
            File.WriteAllText(scene, "<scene><game_entity name='border_min'/><terrain/></scene>");
            using (File.Open(scene, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Equal(RotJoinCheck.IncompletePlayerMap, RotJoinCheck.PlayerMapProblem(module));
            Assert.Null(RotJoinCheck.PlayerMapProblem(module));
        }
        finally
        {
            Directory.Delete(module, recursive: true);
        }
    }

    [Theory]
    [InlineData("<scene><entities><game_entity name='border_min'/></entities><terrain/></scene>", true)]
    [InlineData("<scene><entities/></scene>", false)]
    [InlineData("<scene><entities/><terrain/></scene>", false)]
    [InlineData("<scene><entities><game_entity name='border_min'/></entities></scene>", false)]
    [InlineData("<scene><entities><game_entity name='border_min'/></entities><terrain/>", false)]
    [InlineData("<scene><game_entity name='border_min'/><entities><terrain/></entities></scene>", false)]
    [InlineData("<scene><!-- <game_entity name='border_min'/> --><terrain/></scene>", false)]
    [InlineData("<scene><game_entity name='border_max'/><terrain/></scene>", false)]
    [InlineData(null, false)]
    public void Only_a_readable_player_scene_passes_the_map_check(string? sceneXml, bool ready)
    {
        var module = Directory.CreateTempSubdirectory("rot-player-map-").FullName;
        try
        {
            var sceneFolder = Directory.CreateDirectory(Path.Combine(module, "SceneObj", "Main_map"));
            if (sceneXml != null)
                File.WriteAllText(Path.Combine(sceneFolder.FullName, "scene.xscene"), sceneXml);
            Assert.Equal(ready ? null : RotJoinCheck.IncompletePlayerMap, RotJoinCheck.PlayerMapProblem(module));
        }
        finally
        {
            Directory.Delete(module, recursive: true);
        }
    }
}
