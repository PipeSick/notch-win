# Hatsu Notch V3 — auditoría y plan técnico

Rama de trabajo: `feat/notch-v3` (NotchWin), creada sobre `feat/hatsu-shelf-plus`. Las ramas estables no se tocan. Nada se sube, fusiona ni publica sin autorización explícita.
Estado: **auditoría y plan** (este documento) + **fase 1** (lógica pura con pruebas). La interfaz WinUI de las fases 2 y 3 todavía NO existe.

## 1. Auditoría

### Arquitectura
- **NotchWin.Core** (net10.0, sin Windows): modelos, lógica pura y formato del canal con Hatsu (`Pill/PillFeed`, `Hosting/HostedSession`, `Settings/*`, `Notch/*`). Probada en Linux: **240 pruebas** en `tests/NotchWin.Core.Tests`.
- **NotchWin.App** (WinUI 3, `net10.0-windows10.0.19041.0`): una sola ventana `NotchWindow` dividida en archivos parciales (`xaml`, `Sections`, `Pill`, `Activities`, `Extras`, `Home`, `Tint`, `Resume`…). Los paneles se construyen **en código** sobre un XAML de contenedores; hay ayudantes compartidos en `Views/CardUi.cs` y colores por tema en `Views/Pal.cs` (`IslandPalette` en Core).
- **Compilación:** la interfaz WinUI **no compila en Linux**. Solo la compila el CI de Windows (`build.yml`: job `app`), que se dispara con un push a `feat/**`. Sin push no hay compilación de la interfaz.

### Navegación y estado
- Pestañas por sección (`NotchSections`: home, activity, captures, clipboard, downloads, history, music, timer, calendar, notifications, shelf, camera, system), orden y encendido configurables; máximo 6 visibles y el resto en «Más»; la pestaña elegida muestra su nombre.
- Estado en `NotchViewModel` (música, temporizador, notificaciones, descargas, calendario, portapapeles, actividades, periféricos). Sin MVVM formal: la ventana reconstruye paneles cuando cambian los datos (`RefreshPanes`), con firma para evitar repintados en Inicio.
- Animación de tamaño en `NotchWindowController` (temporizador de 8 ms; resorte al abrir desde `NotchEasing`).
- Material: `GlassBackdrop` (acrílico controlable) + brillo/borde; tinte desde la carátula (`DominantColor`).

### Integración con Hatsu y persistencia
- Canal por tubería (`ModuleHostLink`): ajustes centrales (`config.notch`), eventos de la Barra (`notch.feed`: volumen, brillo, notificaciones, periféricos), actividades (`notch.activity`), consulta de portapapeles de Spotlight, `periph.refresh`.
- Ajustes: central (los que fija Hatsu) + `settings.json` local de NotchWin. Secciones con `Normalize` (conserva orden/estado; Inicio se inserta primera).

### Qué está realmente implementado (y con qué límites)
| Módulo | Existe | Límites reales |
|---|---|---|
| Música | Sí (GSMTC): título, artista, carátula, progreso, play/pausa/siguiente/anterior, **seek**, **letras sincronizadas** (opcionales, Netease), letra en la píldora | **No hay** cola, aleatorio, repetir, «me gusta» ni pestaña «Info» |
| Temporizador | Sí (`CountdownTimer`): iniciar, pausar, reiniciar, presets 5/15/25/50, anillo, aviso al terminar | No hay «cancelar» distinto de reiniciar ni preset 90; el estado «finalizado» solo existe como aviso |
| Calendario | Sí, **solo un .ics** configurado (`WinMac.Calendar`): eventos de hoy, sin recurrencias, zona horaria local | Sin integración con cuentas; sin vista mensual ni crear eventos |
| Notificaciones | Sí: centro de Windows (lectura) + avisos propios; abrir la app (AUMID); botón «Ver» en la píldora; «Limpiar» solo oculta en la Isla | **No hay** «no leído», ni respuesta, ni borrar una sola; Windows no permite contestar un aviso desde fuera |
| Descargas | Sí (`DownloadTracker`): archivos parciales del navegador; velocidad medida | **No se conoce el tamaño total** → ni porcentaje, ni tamaño total, ni tiempo restante, ni pausar/cancelar |
| Portapapeles | Sí: texto e imágenes, buscar, fijar, borrar, limpiar, exclusiones y filtro de contraseñas, persistencia opcional | Sin tipos «enlace»/«archivo»/«código» (se pueden **clasificar** a partir del texto) |
| Capturas | Sí (`CaptureLibrary` + galería): miniaturas, Abrir, Editar, Copiar, Mostrar en carpeta, Eliminar | Sin «por aplicación» |
| En curso / Historial | Sí (`ActivityStore`: estados Active/Paused/Completed/Failed/Cancelled, acciones solo si hay implementación real) | Es la fuente natural para «Completadas» en Descargas |
| Estantería, Cámara, Recursos | Sí | — |
| Inicio | Sí (tablero de tarjetas, sin validar en Windows real) | A rehacer con la jerarquía contextual aprobada |

### Funciones experimentales / dependientes de Windows
Letras (red, opcional); calendario (archivo .ics); notificaciones (permiso de Windows, lo lee la Barra); periféricos (la Barra, con permisos HID); cámara (permiso); cristal (puede caer a color plano sin efectos de transparencia).

### Estado de ramas y árbol
- NotchWin: `feat/hatsu-shelf-plus` con 3 commits locales sin subir (resorte/tarjetas ya subidos hasta be8b666; sin subir: cristal tras suspender, botón «Ver», lectura de periféricos al abrir Inicio). `feat/notch-v3` parte de ahí. Árbol limpio al crear la rama.
- Hatsu: `feat/hatsu-modulos-mission-snap`, con commits locales sin subir. Se crea una rama propia solo si V3 necesita cambios en Hatsu.
- Dependencia: Hatsu fija el commit de NotchWin en `tools/notch-pin.txt`. El pin **no** se toca.

## 2. Mapa mockup → capacidad real
Leyenda: ✅ se puede con lo que hay · 🟡 parcial / derivable · ❌ no existe (no se dibujará como botón activo).

**Inicio**: actividad principal contextual ✅ (música/temporizador/descarga) · actividades secundarias ✅ · «Accesos rápidos» 🟡: **Portapapeles** ✅ (es una pestaña de la Isla); **Captura, Snap, Mission Control, Live Text, Gestures** viven en la Barra de Hatsu: hace falta un tópico nuevo Isla→Barra con lista cerrada de acciones (`notch.quick`) que llame a las funciones que la Barra ya tiene (`MissionControl.Toggle`, `Snap.OpenEditor/Show`, `LiveText.Run`, `Gestures.Run`, captura). Es una integración **real y pequeña**, pero toca Hatsu y es lo único que lo hace. · 📌 «fijar» en la cabecera ❌ (no hay concepto de fijar la Isla abierta) · engranaje ✅ (Personalizar).
**Reproductor compacto persistente** ✅ (ya existe `NowHeader`; se rediseña, sin segundo reproductor).
**Música**: carátula, título, artista, app de origen, progreso, controles ✅ · fondo ambiental ✅ (`DominantColor`) · Letras ✅ · Cola ❌ · Info 🟡 (solo datos que ya tiene: álbum, app, duración) · aleatorio/repetir/«me gusta» ❌ (no se muestran).
**Temporizador**: anillo ✅ · presets ✅ (agregar 90 es trivial) · iniciar/pausar/reanudar/reiniciar ✅ · «cancelar» 🟡 (= reiniciar; no se duplica) · estados sin/preparado/ejecutando/pausado/finalizado ✅ (derivables) · etiqueta «Enfoque» ❌ (se omite).
**Calendario**: cuadrícula del mes ✅ (cálculo) · día seleccionado y eventos del día 🟡 (el .ics ya entrega eventos; hay que pedir otros días) · próximo evento ✅ · botón «+» ❌ (se omite) · sin .ics → estado vacío que explica cómo configurarlo.
**Notificaciones**: icono, app, título, vista previa, tiempo relativo ✅ · no leído ❌ · filtros 🟡: «Todas / Apps de Windows / Avisos de Hatsu» ✅; «Mensajes» ❌ · acciones: abrir app ✅, «Limpiar» ✅; responder ❌.
**Descargas**: nombre, tipo, velocidad, bytes descargados ✅ · progreso/total/ETA/pausar/cancelar ❌ (no se inventan; se muestra una barra **indeterminada** y los bytes) · Completadas ✅ (Historial) con «Abrir» y «Mostrar en carpeta» cuando el módulo las respalda · fallida/cancelada ✅ si la actividad lo informa.
**Portapapeles**: tipo (texto/enlace/código/imagen) ✅ por clasificación · buscar, copiar, fijar, eliminar ✅ · archivos 🟡 (rutas copiadas como texto) · botón de filtro ❌.
**Capturas**: cuadrícula, miniaturas reales, abrir, copiar, mostrar en carpeta, eliminar, editar ✅ · «por aplicación» ❌ · alternar cuadrícula/lista 🟡 (solo visual).

## 3. Plan técnico (por fases; cada fase se puede revertir sola)
**Fase 1 — Lógica pura con pruebas (esta entrega).** `NotchWin.Core/Notch/V3`: prioridad de la actividad principal de Inicio; estados del temporizador; cuadrícula del mes del calendario; clasificación de contenido del portapapeles; tipo de archivo de una descarga y agrupación activas/completadas; tiempo relativo y filtros de notificaciones. Todo verificable en Linux.
**Fase 2 — Sistema visual compartido (WinUI).** Extender `CardUi`/`Pal` (no una segunda arquitectura) con: `NotchSurface` (fondo + borde + tinte), `ModuleHeader`, `SegmentedControl`, `ActionButton` (hover/pressed/disabled/focused), `StatusBadge`, `ProgressIndicator`, `EmptyState`, `ActivityCard`, `ContentPreview`, `CompactMediaPlayer` (reutiliza `NowHeader`). Tipografía e iconografía únicas (Segoe Fluent). Requiere CI de Windows.
**Fase 3 — Vistas.** Inicio contextual (reemplaza el tablero actual, conserva Periféricos/Recursos solo como detalle de «Más» o en su pestaña), luego Música, Temporizador, Calendario, Notificaciones, Descargas, Portapapeles, Capturas. Una vista por commit. Alturas acotadas con desplazamiento interno; sin saltos de tamaño al navegar.
**Fase 4 — Accesos rápidos (Hatsu + NotchWin).** `notch.quick` con lista cerrada de acciones, comprobada contra la Barra; sin él, los accesos que dependen de Hatsu no se muestran (el de Portapapeles sí).
**Fase 5 — Calidad.** Pruebas de lo que se pueda en Linux, CI de Windows, medición de CPU/memoria cuando haya Windows, y lista de validación manual.

### Reglas de rendimiento
Reutilizar la firma de «no repintar si no cambió»; miniaturas con caché y decodificación pequeña (ya existe en galería); sin animaciones permanentes (las barras de audio solo con música sonando); desuscribir eventos al cerrar vistas.

## 4. Bloqueos y decisiones que necesito
1. **No puedo compilar la interfaz aquí.** La fase 2 y 3 se escribirían sin compilar hasta que el CI de Windows las pruebe. Pido autorización para **subir solo la rama de trabajo `feat/notch-v3`** (dispara el CI de NotchWin; sin merge, sin tocar el pin ni las ramas estables). Sin ella sigo con la fase 1 y dejo la interfaz escrita pero sin compilar, marcada así.
2. **Accesos rápidos de Hatsu** requieren una rama en Hatsu (`notch.quick`). ¿Se aprueba?
3. Las funciones marcadas ❌ no se dibujarán. Si alguna es imprescindible, hay que decidir si se construye el backend (por ejemplo tamaño total de descargas: no es posible desde el sistema de archivos).

## 5. Validación pendiente (Windows real)
Todo lo visual y de interacción. Los criterios (17 puntos del encargo) quedan en `docs/NOTCH_V3_VALIDACION.md` al cerrar la fase 3; hasta entonces ninguna vista se da por validada.
