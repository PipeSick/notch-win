# Arquitectura

## Por qué dos proyectos (Core / App)

`NotchWin.Core` no referencia WinUI, WinRT ni ninguna API exclusiva de Windows: es parsing de
JSON, matemática de agregación y acceso a disco con `System.IO`/`System.Text.Json` puros. Eso
tiene una consecuencia práctica importante para este repo: **se puede compilar y testear en
cualquier sistema operativo**, incluyendo el contenedor Linux donde se escribió este código por
primera vez. La lógica con más riesgo de bugs (parsear JSONL ajeno, deduplicar mensajes,
calcular costos) vive ahí y tiene tests reales corriendo en CI.

`NotchWin.App` es la cáscara de WinUI 3: ventanas, tray icon, interop con GSMTC. Depende de
`NotchWin.Core` vía `ProjectReference` y nunca al revés.

## El overlay del notch

La alternativa "correcta" para un notch que se ve como el Dynamic Island de macOS sería una sola
ventana grande, transparente, con *hit-testing* por píxel (solo los píxeles visibles reciben
clics; el resto deja pasar el clic a lo que esté debajo). Eso requiere `WS_EX_LAYERED` +
interceptar `WM_NCHITTEST` devolviendo `HTTRANSPARENT` según el alpha del pixel bajo el cursor —
viable, pero bastante más código Win32 y más superficie para bugs sutiles.

Para v1, `NotchWindowController` usa un enfoque más simple y igual de funcional: la ventana del
sistema operativo *es* literalmente del tamaño de la píldora colapsada o del panel expandido
(`AppWindow.MoveAndResize`), y se repositiona para quedar centrada al tope de la pantalla en
ambos casos. No hay click-through que simular porque, cuando está colapsada, la ventana
simplemente no ocupa el resto de la pantalla. El costo es que la animación de expand/collapse es
un resize instantáneo en vez de una transición de ancho suave — queda como mejora futura (se
puede animar interpolando varios `MoveAndResize` en un `DispatcherTimer`, o migrar al enfoque de
ventana grande + hit-testing si se necesita una transición más vistosa).

Otras dos propiedades del overlay, que la API `AppWindow` administrada no expone y por eso se
resuelven con un puñado de P/Invoke en `Interop/`:

- **No robar foco** (`WS_EX_NOACTIVATE`): click en el notch nunca cambia qué ventana está en
  foreground.
- **No aparecer en Alt+Tab ni en la barra de tareas** (`WS_EX_TOOLWINDOW` + `AppWindow.IsShownInSwitcher = false`).

## Música: por qué GSMTC y no la API Web de Spotify

Windows ya tiene una API de sesión de medios a nivel de sistema operativo
(`Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`, "GSMTC") que
alimenta la superposición de medios del teclado y el Centro de actividades. Spotify, los
navegadores, Windows Media Player y básicamente cualquier app que quiera controles de medios en
el teclado se registran ahí. Usar GSMTC en vez de la API Web de Spotify evita:

- Pedirle al usuario que cree una app en el dashboard de Spotify y maneje OAuth solo para ver qué
  está sonando.
- Que el now-playing deje de funcionar si el usuario escucha música en YouTube Music, Apple Music,
  el navegador, etc. en vez de Spotify.

El costo es que GSMTC es "lo que el sistema operativo sabe", así que si una app no se integra con
el framework de medios de Windows (poco común hoy), no aparecerá. Es el mismo trade-off, en la
dirección inversa, que documenta el propio Vorssaint para su música (también lee el estado a
nivel de sistema, no una API de un solo proveedor).

## Monitor de agentes de IA

Ver [`AI_AGENT_SOURCES.md`](AI_AGENT_SOURCES.md) para el detalle de qué lee cada proveedor, qué
está documentado oficialmente y qué es heurístico. La decisión de diseño compartida por los tres
(`IAgentUsageProvider`) es: **todo se lee de archivos locales, nunca de la red**, igual que
describe el propio Vorssaint para su función equivalente ("Copilot uses local CLI/app session
logs"). Eso significa cero configuración de API keys para el caso común, pero también significa
que los números dependen de un formato de archivo que ninguno de los tres proveedores promete
mantener estable entre versiones — por eso cada parser es defensivo línea por línea (un campo que
falta o cambió de nombre degrada el dato, nunca tira una excepción que mate el polling).
