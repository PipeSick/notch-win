# NotchWin

Un "Dynamic Island" para Windows: un notch flotante arriba al centro de cada pantalla, inspirado en
la isla de [Vorssaint](https://github.com/vorssaint/vorssaint-utils) (macOS). Código abierto, MIT.

No es un fork: Vorssaint es Swift/AppKit/CoreAudio (GPLv3) y nada de eso es reusable en Windows.
Este es código nuevo en C# / WinUI 3 que replica el comportamiento observable descrito en su README
público (no se copió ni se consultó su código fuente).

## Qué hace

La píldora colapsada muestra la portada y el título de lo que suena, un ecualizador, y la cuenta
regresiva del temporizador si hay uno corriendo. Se ensancha unos segundos cuando cambia la canción,
termina un temporizador o una descarga, o un agente de IA termina una tarea larga. Al hacer clic se
expande con pestañas (activables y reordenables desde el engranaje):

| Pestaña | Qué muestra |
|---|---|
| Música | Portada, título, artista, progreso y controles de cualquier reproductor (Spotify, navegador…) vía GSMTC; letra sincronizada opcional |
| Agentes de IA | Tokens, costo estimado, modelos y límites de plan de Claude Code, Codex (varias cuentas), Copilot CLI y OpenCode |
| Temporizador | Cuenta regresiva con anillo de progreso |
| Calendario | Eventos de hoy desde un archivo `.ics` |
| Descargas | Descargas del navegador en curso y terminadas |
| Notificaciones | Historial de lo que NotchWin observó (no lee las notificaciones de otras apps) |
| Estantería | Soltar archivos al notch y arrastrarlos a otra app |
| Cámara | Espejo de la webcam; solo abre la cámara mientras ves esa pestaña (apagada por defecto) |

Hay un notch por monitor.

## Estado del proyecto

- **`src/NotchWin.Core`**: lógica pura, sin dependencias de UI ni de Windows. 41 tests (xUnit) que
  corren en cualquier sistema operativo.
- **`src/NotchWin.App`**: la app WinUI 3. **Compila en Windows**, pero la interfaz todavía no se ha
  visto funcionando en una máquina real: el primer equipo donde se probó tenía Smart App Control,
  que bloquea los ejecutables sin firma. Trátala como una versión temprana.

## Compilar

### Core + tests (cualquier sistema operativo)

```bash
dotnet test tests/NotchWin.Core.Tests
```

### App completa (solo Windows)

Requiere Visual Studio 2022 (o sus Build Tools) con las cargas **.NET Desktop Development** y
**Universal Windows Platform build tools**. **`dotnet build` no sirve para la app**: la generación
de recursos (PRI) necesita tareas de MSBuild que solo vienen con Visual Studio. Usa MSBuild:

```powershell
msbuild src\NotchWin.App\NotchWin.App.csproj /restore /p:Platform=x64
```

Para un ejecutable autocontenido (no necesita instalar .NET):

```powershell
msbuild src\NotchWin.App\NotchWin.App.csproj /restore /t:Publish /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:SelfContained=true /p:PublishDir=publish\
```

Evita rutas largas: con una ruta de proyecto de unos 190 caracteres, el compilador XAML falla sin
mensaje. La app queda en la bandeja del sistema; clic derecho en el ícono → **Ajustes**.

## Privacidad

NotchWin lee archivos locales y no tiene telemetría. Lo único que envía fuera de tu equipo es la
**búsqueda de letras**, y solo si la activas en Ajustes: manda el título y el artista de la canción
a [lrclib.net](https://lrclib.net). Todo lo demás (agentes de IA, calendario, descargas) se lee de
archivos en tu disco.

## Firma del código

Smart App Control de Windows 11 bloquea los programas sin firma. El plan es firmar los releases con
[SignPath Foundation](https://signpath.org); el detalle, los roles y el estado están en
[`docs/CODE_SIGNING.md`](docs/CODE_SIGNING.md). **Mientras eso no esté aprobado, los releases no
están firmados.**

## Estructura

```
src/
  NotchWin.Core/            Lógica sin UI: agentes de IA, calendario .ics, temporizador, descargas,
                            notificaciones, letras (LRC), pricing y settings
  NotchWin.App/             App WinUI 3
    Interop/                P/Invoke puntual (tray icon, forma y estilos de la ventana overlay)
    Modules/                Música (GSMTC), cámara, controlador del notch (animación, multi-monitor)
    Services/               Tray icon, polling de agentes de IA
    ViewModels/, Views/     NotchWindow (+ secciones) y SettingsWindow
tests/NotchWin.Core.Tests/  xUnit, corre en cualquier OS
docs/                       ARCHITECTURE.md, AI_AGENT_SOURCES.md, CODE_SIGNING.md
signpath/                   Configuración del artefacto para la firma
```

## Limitaciones conocidas

Ningún proveedor de IA expone una API pública de "cuánto me queda de mi plan personal"; ver
[`docs/AI_AGENT_SOURCES.md`](docs/AI_AGENT_SOURCES.md). En resumen:

- **Claude Code**: tokens de hoy desde los transcripts locales. El % de plan Pro/Max y su reinicio
  no están disponibles localmente.
- **Codex CLI**: lee el último `token_count` y sus `rate_limits`; el esquema no está documentado y
  ha cambiado entre versiones.
- **Copilot CLI**: conteo heurístico marcado como estimación.
- **OpenCode**: lee el formato de archivos descrito por herramientas de la comunidad; no se pudo
  comprobar contra una instalación real y puede no funcionar con versiones que usen SQLite.
- **Calendario**: lee un `.ics` (Windows no da acceso al calendario a apps sin paquete). No expande
  eventos repetidos.
- **Descargas**: el navegador no informa el tamaño final, así que se ve el tamaño descargado, no un
  porcentaje.
- **Notificaciones**: solo las que NotchWin genera (agente terminó, temporizador, descarga).

## Licencia

MIT (ver [`LICENSE`](LICENSE)). Código nuevo, no derivado de Vorssaint.
