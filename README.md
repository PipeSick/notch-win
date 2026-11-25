# Hatsu Notch (NotchWin)

Una isla flotante para Windows, arriba al centro de cada pantalla: música, temporizador, descargas,
notificaciones, portapapeles, capturas y más, en un solo lugar. Es el módulo «Isla» de
[Hatsu Desktop](https://github.com/PipeSick/hatsu-desktop) y también corre por su cuenta.
Código abierto, MIT. El proyecto se llama **NotchWin** en el código y **Hatsu Notch** de cara al usuario.

Inspirado en la isla de [Vorssaint](https://github.com/vorssaint/vorssaint-utils) (macOS). No es un
fork: es código nuevo en C# / WinUI 3 que replica el comportamiento observable descrito en su README
público (no se copió ni se consultó su código fuente).

## Qué hace

La píldora colapsada muestra la portada y el título de lo que suena, barras de audio y la cuenta
regresiva del temporizador. Se ensancha unos segundos cuando cambia la canción, sube o baja el
volumen o el brillo, llega una notificación o termina una descarga. Al hacer clic se expande con
pestañas (activables y reordenables desde Ajustes):

| Pestaña | Qué muestra |
|---|---|
| Inicio | Tablero de tarjetas: reproductor compacto, temporizador, accesos rápidos, periféricos y recursos |
| Actividades | Lo que está pasando ahora, con prioridad |
| Música | Portada, progreso y controles de cualquier reproductor (GSMTC); letra sincronizada opcional |
| Temporizador | Cuenta regresiva con anillo de progreso |
| Calendario | Cuadrícula del mes y eventos de un archivo `.ics`; notas rápidas |
| Notificaciones | Las de Windows, con clic para abrir la app de origen |
| Descargas | Descargas del navegador y administradas por la Isla |
| Portapapeles | Historial de texto e imágenes (sin guardar contraseñas ni apps excluidas) |
| Capturas | Galería de capturas recientes |
| Estantería | Soltar archivos, carpetas, texto e imágenes y arrastrarlos a otra app |
| Sistema, Cámara | Recursos del equipo; espejo de la webcam (apagada por defecto) |

Hay una isla por monitor. El color de acento, la posición, el vidrio y las pestañas se eligen en los
Ajustes de Hatsu Desktop (o en los de la app suelta).

## Estado del proyecto

- **`src/NotchWin.Core`**: lógica pura, sin UI ni APIs de Windows. Sus pruebas xUnit corren en cualquier sistema operativo.
- **`src/NotchWin.App`**: la app WinUI 3. Compila en Windows (CI), pero **que compile no dice cómo se ve ni que funcione**: la validación visual y funcional la hace una persona en un PC real (ver [`docs/NOTCH_V3_VALIDACION.md`](docs/NOTCH_V3_VALIDACION.md)).
- Dentro de Hatsu Desktop, la Isla recibe sus ajustes del host (`config.notch`) y le devuelve eventos por el canal local `external/winmac` (submódulo fijado a un commit).

## Ramas

| Rama | Qué es |
|---|---|
| `main` | Línea estable: el commit que Hatsu Desktop fija hoy (`42d61c9`). |
| `feat/notch-v3` | **Beta de Notch V3** (Inicio, temporizador, calendario, notificaciones, acento). Es la que prueba la beta privada de la Store. No está fusionada. |
| `feat/hatsu-shelf-plus`, `feat/hatsu-calls` | Ramas de trabajo que Hatsu Desktop usa para sus propias ramas; `feat/notch-v3` las contiene. |
| `chore/submodule-url-hatsu-desktop` | Mismo commit que `main`; se conserva porque otras ramas de Hatsu lo referencian. |
| `claude/*`, `feat/winmac-iteration-*`, `fix/winmac-iteration-6-ui`, `feat/hatsu-notch-rebrand` | Historial de iteraciones anteriores. Todas están contenidas en `main`; se pueden borrar sin perder commits. |

## Compilar

### Core + pruebas (cualquier sistema operativo)

```bash
git submodule update --init --recursive
dotnet test tests/NotchWin.Core.Tests -m:1
```

### App completa (solo Windows)

Requiere el **SDK de .NET 10**; no hace falta Visual Studio.

```powershell
dotnet build src\NotchWin.App\NotchWin.App.csproj -p:Platform=x64
```

Ejecutable autocontenido:

```powershell
dotnet publish src\NotchWin.App\NotchWin.App.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o publish
```

Evita rutas largas: con una ruta de proyecto de unos 190 caracteres el compilador XAML falla sin
mensaje. La app queda en la bandeja del sistema; clic derecho en el ícono → **Ajustes**.

## Privacidad

NotchWin lee archivos locales y no tiene telemetría. Lo único que envía fuera de tu equipo es la
**búsqueda de letras**, y solo si la activas en Ajustes: manda el título y el artista de la canción
a NetEase Music (`music.163.com`). Todo lo demás (agentes de IA, calendario, descargas) se lee de
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
docs/                       Ver docs/README.md
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
