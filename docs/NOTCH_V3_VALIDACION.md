# Hatsu Notch V3 — qué está probado y qué falta (Windows real)

Rama: `feat/notch-v3`. Distingue tres niveles; **ninguna función se da por validada en Windows real mientras no esté marcada en el tercero**.

1. **Pruebas automatizadas (Linux, `NotchWin.Core.Tests` y `WinMac.*.Tests`)**: lógica pura y motores (descargas con servidor local, calendario local, temporizador, bandeja de notificaciones, capacidades multimedia, accesos rápidos de lista cerrada, paleta con contraste).
2. **Compilación Windows (CI de GitHub, `WinUI 3 app (Windows)`)**: la interfaz compila. No dice nada de cómo se ve ni de si funciona.
3. **Validación manual en Windows real**: lo que debe comprobar una persona (lista de abajo).

## Lista de validación manual (los 17 puntos del encargo + lo nuevo)
- [ ] 1. Navegación entre pestañas (iconos, «Más», pestaña activa violeta, teclado Tab/Enter, tooltips).
- [ ] 2. Abrir y cerrar el Notch (resorte al abrir, sin rebote al plegar).
- [ ] 3. Transiciones entre estados (compacta → Inicio → pestaña) sin saltos bruscos de tamaño.
- [ ] 4. Reproductor compacto persistente en las demás pestañas, con música real.
- [ ] 5. Cambiar de canción mientras se navega (título, carátula, progreso y botones se actualizan).
- [ ] 6. Letras sincronizadas (opcionales): línea actual, pausa y reanudación, canción sin letra, cambio de canción; segmento «Letras» ↔ «Info».
- [ ] 7. Temporizador activo al cambiar de pestaña; coincide en cápsula, Inicio y pestaña; Reiniciar y Cancelar; aviso al terminar.
- [ ] 8. Notificaciones nuevas: marcador «nueva», agrupación, filtros, historial de sesión, abrir la app emisora, icono real si Windows lo da.
- [ ] 9. Varias descargas a la vez: administradas (total, ETA, pausar, reanudar, cancelar) y observadas del navegador (indeterminadas).
- [ ] 10. Portapapeles vacío y con contenido de cada tipo (texto, enlace, código, ruta, imagen): buscar, copiar, fijar, editar, eliminar.
- [ ] 11. Galería de capturas vacía y con capturas: orden, búsqueda, vista previa, abrir, copiar, ubicación, eliminar.
- [ ] 12. Monitores con distinto escalado (100 % / 125 % / 150 %).
- [ ] 13. Resoluciones reducidas (que nada se corte; scroll interno).
- [ ] 14. Cambio de tema claro/oscuro con la Isla abierta.
- [ ] 15. Reinicio del host (Hatsu) con el Notch abierto y con descargas administradas en curso.
- [ ] 16. Desactivar módulos (pestañas) desde Personalizar y desde Ajustes de Hatsu: Inicio y los accesos reaccionan.
- [ ] 17. Sin permisos o integraciones: sin .ics, sin notificaciones permitidas, sin letras, sin Hatsu (NotchWin solo): estados vacíos útiles, nada roto.
- [ ] 18. Accesos rápidos de Inicio con Hatsu: Captura, Snap, Mission Control, Live Text, Gestures; con un módulo apagado el acceso no aparece.
- [ ] 19. Calendario: crear, ver y eliminar un evento local; el .ics sigue siendo de solo lectura.
- [ ] 20. Suspender y reanudar con la Isla (ver también `docs/67` de Hatsu).

## Rendimiento (medir con Windows real)
Sin animaciones permanentes (las barras de audio solo con música sonando; el indicador indeterminado de descargas solo mientras se ve la fila). Reconstrucción de Inicio y Descargas condicionada por firma. Miniaturas decodificadas al tamaño dibujado. Pendiente: medir CPU/memoria con la Isla abierta durante 10 minutos.
