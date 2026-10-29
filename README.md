# NotchWin

Clon para Windows de las tres funciones de [Vorssaint](https://github.com/vorssaint/vorssaint-utils) que
no tienen equivalente nativo en Windows ni en [PowerToys](https://github.com/microsoft/PowerToys):

1. **Notch simulado** — una ventana flotante sin bordes, siempre encima, anclada arriba al centro
   de la pantalla, que se expande/contrae.
2. **Módulo de música** — now playing (título, artista, portada) de lo que sea que esté sonando en
   Windows, vía la System Media Transport Controls (la misma API que usa el propio Windows para la
   superposición de medios del teclado).
3. **Monitor de agentes de IA** — uso de tokens, modelos y, cuando el dato existe localmente,
   límites de plan de **Claude Code**, **Codex CLI** y **GitHub Copilot CLI**.

No es un fork: Vorssaint es 100% Swift/AppKit/CoreAudio (GPLv3), nada de eso es reusable en
Windows. Este es código nuevo, en C# / WinUI 3, que replica el comportamiento observable descrito
en su README público (no se copió ni se consultó su código fuente).

## Estado del proyecto

- **`src/NotchWin.Core`**: lógica pura (sin dependencias de UI ni de Windows), **compila y tiene
  tests pasando en Linux/macOS/Windows** con el SDK de .NET 8.
- **`src/NotchWin.App`**: la app WinUI 3 (notch, tray icon, música, settings). Requiere Windows
  10 1903+ con el workload de Windows App SDK — **no se puede compilar ni ejecutar en este
  entorno** (este repo se generó en un contenedor Linux), así que el código fue escrito con
  cuidado siguiendo los patrones estándar de Windows App SDK pero **todavía no se validó en una
  máquina Windows real**. Trátalo como un primer borrador que compila "en teoría"; lo normal es
  que al compilarlo por primera vez en Visual Studio aparezcan uno o dos ajustes menores (versión
  exacta del paquete `Microsoft.WindowsAppSDK`, íconos, etc.).

## Estructura

```
src/
  NotchWin.Core/            Modelos, settings, pricing y los 3 proveedores de uso de IA
    AiAgents/
      ClaudeCode/            Parser de transcripts JSONL de Claude Code
      Codex/                 Parser de rollouts JSONL de Codex CLI (soporta multi-cuenta vía CODEX_HOME)
      Copilot/               Parser heurístico de los logs locales de Copilot CLI
    Music/                   Modelo NowPlayingInfo + interfaz INowPlayingSource
    Pricing/                 Catálogo de precios por modelo (editable vía pricing.json)
    Settings/                AppSettings + almacenamiento en disco
  NotchWin.App/              App WinUI 3 (no compila fuera de Windows)
    Interop/                 P/Invoke puntual (tray icon, estilos de ventana overlay)
    Modules/Music/           Implementación GSMTC de INowPlayingSource
    Modules/Notch/           Controller del overlay (AppWindow, posicionamiento, expand/collapse)
    Services/                Tray icon, polling de agentes de IA
    ViewModels/, Views/      NotchWindow (overlay) y SettingsWindow
tests/
  NotchWin.Core.Tests/       xUnit, corre en cualquier OS (sin Windows App SDK)
docs/
  ARCHITECTURE.md
  AI_AGENT_SOURCES.md        Qué está documentado oficialmente y qué es heurístico, con fuentes
```

## Compilar y correr

### Core + tests (cualquier sistema operativo)

```bash
dotnet test tests/NotchWin.Core.Tests
```

### App completa (requiere Windows)

1. Instala Visual Studio 2022 17.9+ con el workload **".NET Desktop Development"** y el
   componente **"Windows App SDK C# Templates"** (o `dotnet workload install` si usas solo el
   SDK de .NET).
2. Abre `NotchWin.sln` y compila/ejecuta `NotchWin.App` (x64 o ARM64, Debug).
3. La app corre sin ventana principal visible: queda en la bandeja del sistema. Clic derecho en
   el ícono → **Ajustes** para configurar módulos, rutas alternativas y la cuenta de Codex.

No requiere empaquetado MSIX (se compila como ejecutable autocontenido vía
`WindowsAppSDKSelfContained`); un `dotnet publish` en Windows genera un `.exe` distribuible.

## Limitaciones conocidas (léelas antes de confiar en los números)

Ningún proveedor de IA expone una API pública de "cuánto me queda de mi plan personal" — ver
[`docs/AI_AGENT_SOURCES.md`](docs/AI_AGENT_SOURCES.md) para el detalle y las fuentes consultadas.
En resumen:

- **Claude Code**: los tokens de hoy salen de los transcripts JSONL locales (`~/.claude/projects`),
  igual que hacen herramientas como `ccusage`. El **% de plan Pro/Max y su reset no están
  disponibles localmente**; si configuras una Anthropic Admin API key (solo organizaciones) se
  puede consultar `/v1/organizations/usage_report/claude_code`, pero esa no es la cuota de un plan
  personal.
- **Codex CLI**: lee el último evento `token_count` (con su `rate_limits`) de los rollouts JSONL en
  `$CODEX_HOME/sessions`. El esquema **no está documentado oficialmente** y ha cambiado entre
  versiones del CLI — el parser es defensivo (campo faltante = dato ausente, nunca una excepción).
- **GitHub Copilot**: no hay ni API de cuota personal ni esquema documentado para
  `~/.copilot/session-state/*/events.jsonl`. Lo que se muestra es un conteo heurístico de campos
  cuyo nombre contiene "token", marcado explícitamente como estimación
  (`AgentUsageSnapshot.IsEstimate = true`).

## Licencia

MIT (ver [`LICENSE`](LICENSE)) — código 100% nuevo, no derivado de Vorssaint. Vorssaint en sí es
GPLv3, pero eso no aplica aquí porque no se reusó ni una línea de su código. Cambia la licencia si
prefieres otra antes de publicar el repo.
