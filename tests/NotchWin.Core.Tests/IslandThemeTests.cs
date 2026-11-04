using NotchWin.Core.Music;
using NotchWin.Core.Notch;
using Xunit;

namespace NotchWin.Core.Tests;

/// <summary>It7: tema claro completo de la Isla — paleta, contraste y no-regresión del oscuro.</summary>
public class IslandThemeTests
{
    private static Argb O(byte r, byte g, byte b) => Argb.Opaque(r, g, b);

    [Fact]
    public void Dark_theme_colors_are_never_changed()
    {
        foreach (var c in new[] { O(255, 255, 255), O(0x8E, 0x8E, 0x93), new Argb(0x14, 255, 255, 255), O(0xFF, 0xD6, 0x0A), O(0x0A, 0x84, 0xFF), O(0xD9, 0x77, 0x57) })
            Assert.Equal(c, IslandPalette.Map(c, light: false));
    }

    [Fact]
    public void White_text_becomes_graphite_and_white_veils_become_black_veils_in_light()
    {
        Assert.Equal(O(0x1C, 0x1C, 0x1E), IslandPalette.Map(O(255, 255, 255), true));
        var veil = IslandPalette.Map(new Argb(0x14, 255, 255, 255), true); Assert.Equal((0, 0, 0), veil.Rgb); Assert.InRange(veil.A, 8, 0x14);
        Assert.Equal(O(0, 0, 0), IslandPalette.Map(O(0, 0, 0), true));   // negro sobre insignia de color: igual
    }

    [Fact]
    public void Brand_and_status_colors_are_kept_in_light()
    {
        foreach (var c in new[] { O(0xD9, 0x77, 0x57), O(0x10, 0xA3, 0x7F), O(0x8B, 0x5C, 0xF6), O(0xFF, 0x45, 0x3A) }) Assert.Equal(c, IslandPalette.Map(c, true));
    }

    // Peor caso de superficie clara: el tinte al mínimo de opacidad sobre un fondo negro (lo que hay detrás oscuro) y sobre blanco.
    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void Light_text_hierarchy_is_legible_over_the_worst_light_surface(byte behind)
    {
        var surface = IslandPalette.Surface(light: true, behind, GlassTint.LightMinTint);
        Assert.True(IslandPalette.Contrast(IslandPalette.Fg(true), surface) >= 7, "texto principal");
        foreach (uint secondary in new uint[] { 0xFFE5E5EA, 0xFFB4B4BA, 0xFFA1A1A6, 0xFF98989D, 0xFF8E8E93 })   // los grises de texto secundario que usa la Isla
        {
            var dark = O((byte)(secondary >> 16), (byte)(secondary >> 8), (byte)secondary);
            Assert.True(IslandPalette.Contrast(IslandPalette.Map(dark, true), surface) >= 4.5, $"#{secondary:X6} en claro");
        }
    }

    [Fact]
    public void Faint_text_keeps_at_least_decorative_contrast_in_light()
    {
        var surface = IslandPalette.Surface(true, 0, GlassTint.LightMinTint);
        Assert.True(IslandPalette.Contrast(IslandPalette.Map(O(0x63, 0x63, 0x66), true), surface) >= 3.0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void Accent_text_colors_are_legible_in_light(byte behind)
    {
        var surface = IslandPalette.Surface(true, behind, GlassTint.LightMinTint);
        foreach (var accent in new[] { O(0xFF, 0xD6, 0x0A), O(0xFF, 0xB3, 0x40), O(0x0A, 0x84, 0xFF) })
            Assert.True(IslandPalette.Contrast(IslandPalette.Map(accent, true), surface) >= 3.5, $"acento {accent}");
    }

    [Fact]
    public void Dark_theme_text_is_still_legible_over_its_glass()
    {
        var surface = IslandPalette.Surface(false, 0x30, 0.5);
        Assert.True(IslandPalette.Contrast(IslandPalette.Fg(false), surface) >= 7);
    }

    [Fact]
    public void Primary_button_is_inverse_in_each_theme_with_a_readable_icon()
    {
        foreach (bool light in new[] { false, true })
            Assert.True(IslandPalette.Contrast(IslandPalette.Solid(light), IslandPalette.OnSolid(light)) >= 12);
        Assert.NotEqual(IslandPalette.Solid(true), IslandPalette.Solid(false));
    }

    [Fact]
    public void Glass_bases_follow_the_theme_and_keep_the_glass_levels()
    {
        Assert.True(IslandPalette.Fg(true).R < 64 && IslandPalette.GlassBase(true, false).R > 200);
        Assert.True(IslandPalette.GlassBase(false, false).R < 64);
        // el control «Cristal» sigue mandando: sin cambios en la curva ni en los mínimos
        var (t0, _) = GlassTint.For(0, false, true); var (t1, _) = GlassTint.For(1, false, true);
        Assert.True(t1 > t0 || t0 >= 0.78f);
    }

    [Fact]
    public void Unknown_neutral_grays_mirror_their_luminosity()
    {
        Assert.Equal(O(0xEF, 0xEF, 0xEF), IslandPalette.Map(O(0x10, 0x10, 0x10), true));
    }

    // ── Reproducir/Pausar: un solo glifo por estado, el mismo en todas las tarjetas
    [Theory]
    [InlineData(true, "", "Pausar")]
    [InlineData(false, "", "Reproducir")]
    public void Play_pause_glyph_shows_the_action_the_button_will_do(bool playing, string glyph, string tip)
    {
        Assert.Equal(glyph, MediaGlyphs.PlayPause(playing)); Assert.Equal(tip, MediaGlyphs.PlayPauseTip(playing));
    }
}

/// <summary>It7: las tablas de color por tema del XAML y la paleta de Core son la misma (si alguien cambia una sin la otra, esto avisa).</summary>
public class IslandXamlThemeTests
{
    private static string? FindXaml()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "src", "NotchWin.App", "Views", "NotchWindow.xaml");
            if (File.Exists(f)) return f;
        }
        return null;
    }

    private static Argb ParseArgb(string hex) => new(Convert.ToByte(hex[0..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16), Convert.ToByte(hex[6..8], 16));

    [Fact]
    public void Every_light_resource_in_the_xaml_is_the_palette_mapping_of_its_dark_one()
    {
        var path = FindXaml(); if (path is null) return;   // fuera del repositorio completo no hay nada que comparar
        var xaml = File.ReadAllText(path);
        var dark = System.Text.RegularExpressions.Regex.Match(xaml, "x:Key=\"Dark\">(.*?)</ResourceDictionary>", System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
        var light = System.Text.RegularExpressions.Regex.Match(xaml, "x:Key=\"Light\">(.*?)</ResourceDictionary>", System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
        var rx = new System.Text.RegularExpressions.Regex("x:Key=\"NW_([0-9A-Fa-f]{8})\" Color=\"#([0-9A-Fa-f]{8})\"");
        var d = rx.Matches(dark).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value); var l = rx.Matches(light).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        Assert.NotEmpty(d);
        foreach (var (key, darkHex) in d)
        {
            Assert.Equal(key, darkHex, ignoreCase: true);   // el oscuro es el color de siempre
            Assert.Equal(IslandPalette.Map(ParseArgb(darkHex), true), ParseArgb(l[key]));
        }
        // sin literales «oscuros» sueltos en los controles de texto/fondo: todo pasa por un recurso de tema
        Assert.DoesNotContain("Foreground=\"White\"", xaml);
    }
}

/// <summary>It7: el estado de Reproducir/Pausa que ven todas las vistas es el real, aunque las lecturas terminen desordenadas o el comando no surta efecto.</summary>
public class PlaybackSyncTests
{
    private static NowPlayingInfo Info(bool playing, string title = "T") => new(title, "A", playing, TimeSpan.Zero, TimeSpan.FromMinutes(3), "Spotify.exe", null);

    [Fact]
    public void An_older_read_finishing_late_never_overwrites_a_newer_one()
    {
        var gate = new PlaybackPublishGate();
        long playingRead = gate.Begin(), pausedRead = gate.Begin();   // la de «reproduciendo» empezó antes, la de «pausa» después
        Assert.True(gate.TryPublish(pausedRead));    // la de pausa termina primero
        Assert.False(gate.TryPublish(playingRead));  // la antigua llega tarde: se descarta
    }

    [Fact]
    public void Reads_finishing_in_order_are_all_published()
    {
        var gate = new PlaybackPublishGate(); long a = gate.Begin(), b = gate.Begin();
        Assert.True(gate.TryPublish(a)); Assert.True(gate.TryPublish(b));
    }

    [Fact]
    public void A_session_that_closed_discards_reads_still_in_flight()
    {
        var gate = new PlaybackPublishGate(); long inflight = gate.Begin();
        long closed = gate.Begin(); Assert.True(gate.TryPublish(closed));   // la sesión se cerró: se publica «nada»
        Assert.False(gate.TryPublish(inflight));                          // y la lectura vieja no la resucita
    }

    [Fact]
    public void Only_visible_differences_trigger_a_republish()
    {
        Assert.False(PlaybackReconcile.Differs(Info(true), Info(true)));
        Assert.True(PlaybackReconcile.Differs(Info(true), Info(false)));          // el comando sí cambió el estado (o lo cambió alguien fuera)
        Assert.True(PlaybackReconcile.Differs(Info(true, "A"), Info(true, "B"))); // otra pista
        Assert.True(PlaybackReconcile.Differs(Info(true), null));                 // la sesión se cerró
        Assert.True(PlaybackReconcile.Differs(null, Info(false)));                // apareció una sesión
        Assert.False(PlaybackReconcile.Differs(null, null));
    }

    [Fact]
    public void After_a_command_the_state_is_read_again_several_times()
    {
        Assert.True(PlaybackReconcile.AfterCommandDelaysMs.Length >= 2);
        Assert.True(PlaybackReconcile.AfterCommandDelaysMs.SequenceEqual(PlaybackReconcile.AfterCommandDelaysMs.OrderBy(x => x)));
    }
}
